using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using jaytwo.RuntimeRevelation;
using jaytwo.Subprocess.Shim;
using jaytwo.Subprocess.Streaming;

namespace jaytwo.Subprocess
{
    public class CliCommandBuilder
    {
        public CliCommandBuilder()
            : this(string.Empty, Array.Empty<object>(), RuntimeInformation.Current)
        {
        }

        public CliCommandBuilder(string fileName)
            : this(fileName, Array.Empty<object>(), RuntimeInformation.Current)
        {
        }

        public CliCommandBuilder(string fileName, params string[] initialArguments)
            : this(fileName, initialArguments, RuntimeInformation.Current)
        {
        }

        public CliCommandBuilder(string fileName, params object[] initialArguments)
            : this(fileName, initialArguments, RuntimeInformation.Current)
        {
        }

        internal CliCommandBuilder(string fileName, object[] initialArguments, RuntimeInformation runtimeInfo)
        {
            WithFileName(fileName);
            WithArguments(initialArguments);
            RuntimeInfo = runtimeInfo;
        }

        public RuntimeInformation RuntimeInfo { get; }

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

        public CliCommandBuilder WithRuntimeCondition(Func<RuntimeInformation, bool> runtimeCondition, Action<CliCommandBuilder> action)
        {
            var runtimeConditionMet = runtimeCondition.Invoke(RuntimeInfo);

            if (runtimeConditionMet)
            {
                action.Invoke(this);
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

        public CliCommandBuilder WithArgument(string format, params object[] args)
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

        public CliCommand GetCommand()
        {
            var result = new CliCommand()
            {
                FileName = FileName,
                Arguments = GetInlineArguments(Arguments),
                WorkingDirectory = WorkingDirectory,
                Timeout = Timeout,
                ExpectedExitCodes = ExpectedExitCodes?.ToArray() ?? Array.Empty<int>(),
                Secrets = Secrets,
                StandardInput = StandardInput,
                StandardInputStream = StandardInputStream,
                StandardInputEncoding = StandardInputEncoding,
                StandardOutputEncoding = StandardOutputEncoding,
                StandardErrorEncoding = StandardErrorEncoding,
                OutputDrainTimeout = OutputDrainTimeout,
            };

            foreach (var environmentVariable in EnvironmentVariables)
            {
                result.Environment[environmentVariable.Key] = environmentVariable.Value;
            }

            return result;
        }

        /// <summary>
        /// Builds launch settings for a <see cref="CliProcessSession"/> from this builder's file name, quoted arguments,
        /// working directory, environment, secrets and encodings. One-shot settings (stdin, timeout, expected exit codes,
        /// output drain timeout) are not applied and cause <see cref="InvalidOperationException"/> if configured.
        /// </summary>
        public CliProcessOptions GetProcessOptions()
        {
            var unsupported = GetUnsupportedProcessOptionSettings();
            if (unsupported.Count > 0)
            {
                throw new InvalidOperationException(
                    "GetProcessOptions cannot apply these one-shot settings: " + string.Join(", ", unsupported) +
                    ". Clear them for a session, or use GetCommand() for one-shot execution.");
            }

            var result = new CliProcessOptions
            {
                FileName = FileName,
                Arguments = GetInlineArguments(Arguments),
                WorkingDirectory = WorkingDirectory,
                Secrets = Secrets?.ToList() ?? new List<string>(),
                StandardInputEncoding = StandardInputEncoding,
                StandardOutputEncoding = StandardOutputEncoding,
                StandardErrorEncoding = StandardErrorEncoding,
            };

            foreach (var environmentVariable in EnvironmentVariables)
            {
                result.Environment[environmentVariable.Key] = environmentVariable.Value;
            }

            return result;

            List<string> GetUnsupportedProcessOptionSettings()
            {
                var names = new List<string>();

                if (StandardInput != null)
                {
                    names.Add(nameof(StandardInput));
                }

                if (StandardInputStream != null)
                {
                    names.Add(nameof(StandardInputStream));
                }

                if (Timeout.HasValue)
                {
                    names.Add(nameof(Timeout));
                }

                if (ExpectedExitCodes != null && ExpectedExitCodes.Count > 0)
                {
                    names.Add(nameof(ExpectedExitCodes));
                }

                if (OutputDrainTimeout.HasValue)
                {
                    names.Add(nameof(OutputDrainTimeout));
                }

                return names;
            }
        }

        public override string ToString()
        {
            return GetCommand().ToString();
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
}
