using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using jaytwo.Subprocess.Streaming;
using Microsoft.Extensions.Logging;

namespace jaytwo.Subprocess;

public class CliCommand
{
    public string? Arguments { get; set; }

    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

    public int[] ExpectedExitCodes { get; set; } = new int[] { };

    public string? FileName { get; set; }

    public TimeSpan? Timeout { get; set; }

    public string? WorkingDirectory { get; set; }

    public IList<string> Secrets { get; set; } = new List<string>();

    /// <summary>
    /// Text written to the child's stdin, which is then closed. Null (with no <see cref="StandardInputStream"/>) leaves stdin
    /// un-redirected: the child inherits the host's stdin. Use an empty string to give the child immediate EOF.
    /// </summary>
    public string? StandardInput { get; set; }

    /// <summary>
    /// Bytes copied to the child's stdin, from the stream's current position to its end, after which stdin is closed.
    /// The caller owns the stream: it is never disposed or rewound, and must not be used by the caller until execution returns.
    /// Mutually exclusive with <see cref="StandardInput"/>.
    /// </summary>
    public Stream? StandardInputStream { get; set; }

    /// <summary>
    /// Encoding for <see cref="StandardInput"/> text. Default is UTF-8 without a byte order mark. Not used for <see cref="StandardInputStream"/>.
    /// </summary>
    public Encoding? StandardInputEncoding { get; set; }

    /// <summary>
    /// Encoding used to decode stdout. Null keeps the platform default of System.Diagnostics.Process.
    /// </summary>
    public Encoding? StandardOutputEncoding { get; set; }

    /// <summary>
    /// Encoding used to decode stderr. Null keeps the platform default of System.Diagnostics.Process.
    /// </summary>
    public Encoding? StandardErrorEncoding { get; set; }

    /// <summary>
    /// How long to wait, after the process has ended (or been terminated), for stdout/stderr/stdin to finish. Default is 5 seconds.
    /// This bounds the wait when a descendant still holds the redirected pipe handles.
    /// </summary>
    public TimeSpan? OutputDrainTimeout { get; set; }

    public TimeSpan? TerminateTimeout { get; set; }

    public ILogger? Logger { get; set; }

    public static CliCommand Build(Action<CliCommandBuilder> configure)
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var builder = new CliCommandBuilder();
        configure(builder);
        return builder.GetCommand();
    }

    public static CliCommandResult Run(Action<CliCommandBuilder> configure) => Build(configure).Run();

    public static CliCommandResult Run(Action<CliCommandBuilder> configure, bool suppressCommandLineExceptions)
        => Build(configure).Run(suppressCommandLineExceptions);

    public static CliCommandResult Run(Action<CliCommandBuilder> configure, CancellationToken cancellationToken)
        => Build(configure).Run(cancellationToken);

    public static CliCommandResult Run(Action<CliCommandBuilder> configure, bool suppressCommandLineExceptions, CancellationToken cancellationToken)
        => Build(configure).Run(suppressCommandLineExceptions, cancellationToken);

    public static Task<CliCommandResult> RunAsync(Action<CliCommandBuilder> configure) => Build(configure).RunAsync();

    public static Task<CliCommandResult> RunAsync(Action<CliCommandBuilder> configure, bool suppressCommandLineExceptions)
        => Build(configure).RunAsync(suppressCommandLineExceptions);

    public static Task<CliCommandResult> RunAsync(Action<CliCommandBuilder> configure, CancellationToken cancellationToken)
        => Build(configure).RunAsync(cancellationToken);

    public static Task<CliCommandResult> RunAsync(Action<CliCommandBuilder> configure, bool suppressCommandLineExceptions, CancellationToken cancellationToken)
        => Build(configure).RunAsync(suppressCommandLineExceptions, cancellationToken);

    public static CliProcessSession Start(Action<CliCommandBuilder> configure) => Build(configure).Start();

    public CliCommandResult Run() => Run(false, CancellationToken.None);

    public CliCommandResult Run(bool suppressCommandLineExceptions) => Run(suppressCommandLineExceptions, CancellationToken.None);

    public CliCommandResult Run(CancellationToken cancellationToken) => Run(false, cancellationToken);

    public CliCommandResult Run(bool suppressCommandLineExceptions, CancellationToken cancellationToken)
    {
        // Use the async core off the caller's synchronization context.
        return Task.Run(() => RunAsync(suppressCommandLineExceptions, cancellationToken), CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task<CliCommandResult> RunAsync() => RunAsync(false, CancellationToken.None);

    public Task<CliCommandResult> RunAsync(bool suppressCommandLineExceptions) => RunAsync(suppressCommandLineExceptions, CancellationToken.None);

    public Task<CliCommandResult> RunAsync(CancellationToken cancellationToken) => RunAsync(false, cancellationToken);

    public async Task<CliCommandResult> RunAsync(bool suppressCommandLineExceptions, CancellationToken cancellationToken)
    {
        if (TerminateTimeout.HasValue)
        {
            throw new InvalidOperationException("Run cannot apply TerminateTimeout. Clear it for one-shot execution, or use Start for a session.");
        }

        try
        {
            return await ProcessRunner.RunAsync(this, cancellationToken).ConfigureAwait(false);
        }
        catch (SubprocessException exception) when (ShouldSuppress(exception, suppressCommandLineExceptions))
        {
            return exception.Result;
        }
    }

    public CliProcessSession Start()
    {
        var unsupported = new List<string>();
        if (StandardInput != null)
        {
            unsupported.Add(nameof(StandardInput));
        }

        if (StandardInputStream != null)
        {
            unsupported.Add(nameof(StandardInputStream));
        }

        if (Timeout.HasValue)
        {
            unsupported.Add(nameof(Timeout));
        }

        if (ExpectedExitCodes != null && ExpectedExitCodes.Length > 0)
        {
            unsupported.Add(nameof(ExpectedExitCodes));
        }

        if (OutputDrainTimeout.HasValue)
        {
            unsupported.Add(nameof(OutputDrainTimeout));
        }

        if (unsupported.Count > 0)
        {
            throw new InvalidOperationException("Start cannot apply one-shot settings: " + string.Join(", ", unsupported) + ". Clear them for a session, or use Run.");
        }

        return CliProcessSession.Create(this);
    }

    public override string ToString() => MaskSecrets($"{FileName} {Arguments}".Trim());

    internal static bool ShouldSuppress(SubprocessException exception, bool suppressCommandLineExceptions)
        => suppressCommandLineExceptions && !exception.Result.Canceled;

    internal string MaskSecrets(string? value) => ProcessLaunchSpec.MaskSecrets(Secrets, value);

    internal ProcessLaunchSpec ToLaunchSpec() => new ProcessLaunchSpec
    {
        FileName = FileName,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        Environment = Environment,
        Secrets = Secrets,
        StandardOutputEncoding = StandardOutputEncoding,
        StandardErrorEncoding = StandardErrorEncoding,
    };
}
