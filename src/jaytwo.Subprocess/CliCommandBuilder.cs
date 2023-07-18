using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using jaytwo.RuntimeRevelation;
using jaytwo.Subprocess.Shim;

namespace jaytwo.Subprocess
{
    public class CliCommandBuilder
    {
        public CliCommandBuilder()
            : this(null, null, RuntimeInformation.Current)
        {
        }

        public CliCommandBuilder(string fileName)
            : this(fileName, null, RuntimeInformation.Current)
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

        public IList<int> ExpectedExitCodes { get; set; }

        public string FileName { get; set; }

        public TimeSpan? Timeout { get; set; }

        public string WorkingDirectory { get; set; }

        public IList<string> Secrets { get; set; } = new List<string>();

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
            return WithArgument(argument?.ToString());
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

        public CliCommand GetCommand()
        {
            var result = new CliCommand()
            {
                FileName = FileName,
                Arguments = GetInlineArguments(Arguments),
                WorkingDirectory = WorkingDirectory,
                Timeout = Timeout,
                ExpectedExitCodes = ExpectedExitCodes?.ToArray(),
                Secrets = Secrets,
            };

            foreach (var environmentVariable in EnvironmentVariables)
            {
                result.Environment[environmentVariable.Key] = environmentVariable.Value;
            }

            return result;
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
