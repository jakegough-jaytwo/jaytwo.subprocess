using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using jaytwo.Subprocess.Shim;
using Microsoft.Extensions.Logging;

namespace jaytwo.Subprocess;

public class CliCommandBuilder
{
    public CliCommandBuilder()
        : this(string.Empty, Array.Empty<object>())
    {
    }

    public CliCommandBuilder(string fileName)
        : this(fileName, Array.Empty<object>())
    {
    }

    public CliCommandBuilder(string fileName, params string[] initialArguments)
    {
        WithFileName(fileName);
        WithArguments(initialArguments);
    }

    public CliCommandBuilder(string fileName, params object[] initialArguments)
    {
        WithFileName(fileName);
        WithArguments(initialArguments);
    }

    public IList<string> Arguments { get; set; } = new List<string>();

    public IDictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string>();

    public IList<int> ExpectedExitCodes { get; set; } = Array.Empty<int>();

    public string? FileName { get; set; }

    public TimeSpan? Timeout { get; set; }

    public string? WorkingDirectory { get; set; }

    public IList<string> Secrets { get; set; } = new List<string>();

    public string? StandardInput { get; set; }

    public Stream? StandardInputStream { get; set; }

    public Encoding? StandardInputEncoding { get; set; }

    public Encoding? StandardOutputEncoding { get; set; }

    public Encoding? StandardErrorEncoding { get; set; }

    public TimeSpan? OutputDrainTimeout { get; set; }

    /// <summary>
    /// How long a session waits after kill for the process to exit. Rejected by Run and RunAsync.
    /// </summary>
    public TimeSpan? TerminateTimeout { get; set; }

    public ILogger? Logger { get; set; }

    public CliCommandBuilder WithLogger(ILogger? logger)
    {
        Logger = logger;
        return this;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on this builder when <paramref name="condition"/> is true.
    /// Use for concise platform (or other) branches without leaving the fluent chain; the caller owns the condition
    /// (for example <c>RuntimeInformation.IsOSPlatform(OSPlatform.Windows)</c>).
    /// </summary>
    public CliCommandBuilder When(bool condition, Action<CliCommandBuilder> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (condition)
        {
            action(this);
        }

        return this;
    }

    public CliCommandBuilder WithArguments(object[] arguments)
    {
        if (arguments != null)
        {
            foreach (var argument in arguments)
            {
                WithArgument(argument);
            }
        }

        return this;
    }

    public CliCommandBuilder WithArguments(string[] arguments)
    {
        if (arguments != null)
        {
            foreach (var argument in arguments)
            {
                WithArgument(argument);
            }
        }

        return this;
    }

    public CliCommandBuilder WithArgument(object argument)
    {
        return WithArgument(argument?.ToString() ?? string.Empty);
    }

    public CliCommandBuilder WithArgument(string argument)
    {
        Arguments.Add(argument);
        return this;
    }

    /// <summary>
    /// Formats a single argument with <see cref="string.Format(string, object[])"/>, then adds it.
    /// Prefer discrete <see cref="WithArgument(string)"/> calls for separate argv tokens;
    /// use this when the tool expects one script or expression string (for example <c>node -e</c>).
    /// </summary>
    public CliCommandBuilder WithFormattedArgument(string format, params object[] args)
    {
        var formatted = string.Format(format, args);
        return WithArgument(formatted);
    }

    public CliCommandBuilder WithExpectedExitCode(int expectedExitCode)
    {
        return WithExpectedExitCodes(new[] { expectedExitCode });
    }

    public CliCommandBuilder WithExpectedExitCodes(params int[] expectedExitCodes)
    {
        ExpectedExitCodes = expectedExitCodes;
        return this;
    }

    public CliCommandBuilder WithFileName(string fileName)
    {
        FileName = fileName;
        return this;
    }

    public CliCommandBuilder WithTimeout(TimeSpan? timeout)
    {
        Timeout = timeout;
        return this;
    }

    public CliCommandBuilder WithWorkingDirectory(string workingDirectory)
    {
        WorkingDirectory = workingDirectory;
        return this;
    }

    public CliCommandBuilder WithEnvironmentVariable(string key, string value)
    {
        EnvironmentVariables[key] = value;
        return this;
    }

    public CliCommandBuilder WithEnvironmentVariables(IDictionary<string, string> environmentVariables)
    {
        foreach (var environmentVariable in environmentVariables)
        {
            EnvironmentVariables[environmentVariable.Key] = environmentVariable.Value;
        }

        return this;
    }

    public CliCommandBuilder WithSecret(string secret)
    {
        Secrets.Add(secret);

        return this;
    }

    public CliCommandBuilder WithStandardInput(string text, Encoding? encoding = null)
    {
        StandardInput = text;
        StandardInputStream = null;
        StandardInputEncoding = encoding;
        return this;
    }

    public CliCommandBuilder WithStandardInput(Stream stream)
    {
        StandardInputStream = stream;
        StandardInput = null;
        return this;
    }

    public CliCommandBuilder WithStandardOutputEncoding(Encoding encoding)
    {
        StandardOutputEncoding = encoding;
        return this;
    }

    public CliCommandBuilder WithStandardErrorEncoding(Encoding encoding)
    {
        StandardErrorEncoding = encoding;
        return this;
    }

    public CliCommandBuilder WithOutputDrainTimeout(TimeSpan? outputDrainTimeout)
    {
        OutputDrainTimeout = outputDrainTimeout;
        return this;
    }

    public CliCommandBuilder WithTerminateTimeout(TimeSpan? terminateTimeout)
    {
        TerminateTimeout = terminateTimeout;
        return this;
    }

    public CliCommand GetCommand()
    {
        var result = new CliCommand()
        {
            FileName = FileName,
            Arguments = GetInlineArguments(Arguments),
            WorkingDirectory = WorkingDirectory,
            Timeout = Timeout,
            ExpectedExitCodes = ExpectedExitCodes?.ToArray() ?? Array.Empty<int>(),
            Secrets = Secrets?.ToList() ?? new List<string>(),
            StandardInput = StandardInput,
            StandardInputStream = StandardInputStream,
            StandardInputEncoding = StandardInputEncoding,
            StandardOutputEncoding = StandardOutputEncoding,
            StandardErrorEncoding = StandardErrorEncoding,
            OutputDrainTimeout = OutputDrainTimeout,
            TerminateTimeout = TerminateTimeout,
            Logger = Logger,
        };

        foreach (var environmentVariable in EnvironmentVariables)
        {
            result.Environment[environmentVariable.Key] = environmentVariable.Value;
        }

        return result;
    }

    public override string ToString()
    {
        // Format the launch line directly so session-only settings (TerminateTimeout) do not make diagnostics throw.
        return ProcessLaunchSpec.MaskSecrets(Secrets, $"{FileName} {GetInlineArguments(Arguments)}".Trim());
    }

    private static string GetInlineArguments(IList<string> argumentList)
    {
        var stringBuilder = new StringBuilder();

        if (argumentList != null && argumentList.Count > 0)
        {
            foreach (string argument in argumentList)
            {
                PasteArguments.AppendArgument(stringBuilder, argument);
            }
        }

        return stringBuilder.ToString();
    }
}
