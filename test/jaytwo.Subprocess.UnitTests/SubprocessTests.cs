using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.Subprocess.UnitTests
{
    public class SubprocessTests
    {
        private readonly ITestOutputHelper _output;

        public SubprocessTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(15)]
        [InlineData(31)]
        [InlineData(63)]
        [InlineData(127)]
        [InlineData(255)]
        public async Task RunAsync_captures_exit_code(int exitCode)
        {
            // arrange
            var command = new CliCommandBuilder("node", "-e") // TODO: find something that we can invoke via CLI that will exist on both windows and non-windows systems
                .WithFormattedArgument("process.exit({0})", exitCode)
                .WithExpectedExitCodes(exitCode)
                .GetCommand();

            // act
            var result = await command.RunAsync();

            // (log)
            _output.WriteLine("Command: {0}", result.Command);
            _output.WriteLine("ExitCode: {0}", result.ExitCode);
            _output.WriteLine("StandardOutput: {0}", result.StandardOutput?.Trim());
            _output.WriteLine("StandardError: {0}", result.StandardError?.Trim());

            // assert
            Assert.Equal(exitCode, result.ExitCode);
        }

        [Fact]
        public async Task RunAsync_processes_input_arguments_and_captures_standard_output()
        {
            // arrange
            var message = "hello world";

            var command = new CliCommandBuilder("node", "-e")
                .WithFormattedArgument("console.log('{0}')", message)
                .GetCommand();

            // act
            var result = await command.RunAsync();

            // (log)
            _output.WriteLine("Command: {0}", result.Command);
            _output.WriteLine("ExitCode: {0}", result.ExitCode);
            _output.WriteLine("StandardOutput: {0}", result.StandardOutput?.Trim());
            _output.WriteLine("StandardError: {0}", result.StandardError?.Trim());

            // assert
            Assert.Equal(message, result.StandardOutput?.Trim());
            Assert.Empty(result.StandardError?.Trim());
        }

        [Theory]
        [InlineData("abc", null)]
        [InlineData("abc def", null)]
        [InlineData("abc\"def", null)]
        [InlineData("abc'def", null)]
        [InlineData(@"abc\def", null)]
        [InlineData("abc\ndef", null)]
        [InlineData("abc\rdef", null)]
        [InlineData("`-=~!@#$%^&*()_+", null)]
        [InlineData("[];,./{}|:<>?", null)]
        public async Task RunAsync_with_special_characters(string message, string expectedOutput)
        {
            // arrange
            var messageReadyForConsoleLog = message
                .Replace("\\", "\\\\") // slashes first so we don't redo them as part of escaping quotes or newlines
                .Replace("'", @"\'") // because we're already inside single quotes
                .Replace("\n", @"\n") // we have to pass newlines as strings to be evaluated inside the quotes
                .Replace("\r", @"\r");

            var command = new CliCommandBuilder("node", "-e")
                .WithFormattedArgument("console.log('{0}')", messageReadyForConsoleLog)
                .GetCommand();

            // (log)
            _output.WriteLine("Command: {0}", command);

            // act
            var result = await command.RunAsync();

            // (log)
            _output.WriteLine("StandardOutput: {0}", result.StandardOutput?.Trim());

            // assert
            // output is captured verbatim, so embedded newline characters round-trip unchanged
            expectedOutput = expectedOutput ?? message;

            Assert.Equal(expectedOutput, result.StandardOutput?.Trim());
        }

        [Fact]
        public async Task RunAsync_input_arguments_and_captures_standard_error()
        {
            // arrange
            var message = "hello world";

            var command = new CliCommandBuilder("node", "-e")
                .WithFormattedArgument("console.error('{0}')", message)
                .GetCommand();

            // act
            var result = await command.RunAsync();

            // (log)
            _output.WriteLine("Command: {0}", result.Command);
            _output.WriteLine("ExitCode: {0}", result.ExitCode);
            _output.WriteLine("StandardOutput: {0}", result.StandardOutput?.Trim());
            _output.WriteLine("StandardError: {0}", result.StandardError?.Trim());

            // assert
            Assert.Empty(result.StandardOutput?.Trim());
            Assert.Equal(message, result.StandardError?.Trim());
        }

        [Theory]
        [InlineData(5000, 100, true)]
        [InlineData(100, 5000, false)]
        public async Task RunAsync_processes_timeout(double sleepMilliseconds, double timeoutMilliseconds, bool expectedTimedOut)
        {
            // arrange
            var command = new CliCommandBuilder("node", "-e")
                .WithFormattedArgument("setTimeout(function() {{ console.log('exiting as scheduled...'); process.exit(123); }}, {0});", sleepMilliseconds)
                .WithTimeout(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                .GetCommand();

            // act
            var result = await command.RunAsync(true);

            // (log)
            _output.WriteLine("Command: {0}", result.Command);
            _output.WriteLine("ExitCode: {0}", result.ExitCode);
            _output.WriteLine("Duration: {0:n0}ms", result.Duration.TotalMilliseconds);
            _output.WriteLine("StandardOutput: {0}", result.StandardOutput?.Trim());
            _output.WriteLine("StandardError: {0}", result.StandardError?.Trim());

            // assert
            Assert.Equal(expectedTimedOut, result.Duration.TotalMilliseconds > timeoutMilliseconds);
            Assert.Equal(expectedTimedOut, result.TimedOut);
        }

        [Fact]
        public void Run_works_non_async()
        {
            // arrange
            var message = "hello world";

            var command = new CliCommandBuilder("node", "-e")
                .WithFormattedArgument("console.log('{0}')", message)
                .GetCommand();

            // act
            var result = command.Run();

            // (log)
            _output.WriteLine("Command: {0}", result.Command);
            _output.WriteLine("ExitCode: {0}", result.ExitCode);
            _output.WriteLine("StandardOutput: {0}", result.StandardOutput?.Trim());
            _output.WriteLine("StandardError: {0}", result.StandardError?.Trim());

            // assert
            Assert.Equal(message, result.StandardOutput?.Trim());
            Assert.Empty(result.StandardError?.Trim());
        }

        [Theory]
        [InlineData(5000, 100, true)]
        [InlineData(100, 5000, false)]
        public void Run_processes_timeout_non_async(double sleepMilliseconds, double timeoutMilliseconds, bool expectedTimedOut)
        {
            // arrange
            var command = new CliCommandBuilder("node", "-e")
                .WithFormattedArgument("setTimeout(function() {{ console.log('exiting as scheduled...'); process.exit(123); }}, {0});", sleepMilliseconds)
                .WithTimeout(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                .GetCommand();

            // act
            var result = command.Run(true);

            // (log)
            _output.WriteLine("Command: {0}", result.Command);
            _output.WriteLine("ExitCode: {0}", result.ExitCode);
            _output.WriteLine("Duration: {0:n0}ms", result.Duration.TotalMilliseconds);
            _output.WriteLine("StandardOutput: {0}", result.StandardOutput?.Trim());
            _output.WriteLine("StandardError: {0}", result.StandardError?.Trim());

            // assert
            Assert.Equal(expectedTimedOut, result.Duration.TotalMilliseconds > timeoutMilliseconds);
            Assert.Equal(expectedTimedOut, result.TimedOut);
        }

        [Fact]
        public void Secrets_are_not_written_in_ToString()
        {
            // arrange
            var secret = "rosebudwasthesled";

            var command = new CliCommandBuilder("node", "-e")
                .WithFormattedArgument("console.log('hello, the secret is: {0}')", secret)
                .WithSecret(secret)
                .GetCommand();

            // act
            var commandText = command.ToString();

            // assert
            Assert.DoesNotContain(secret, commandText);
            Assert.Equal("node -e \"console.log('hello, the secret is: ****')\"", commandText);
        }
    }
}
