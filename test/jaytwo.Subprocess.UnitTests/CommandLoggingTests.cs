using System;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class CommandLoggingTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task RunAsync_masks_and_truncates_command_and_output()
    {
        // arrange
        var logger = new RecordingLogger();
        var secret = "private-token";
        var value = secret + new string('x', 3000);
        var command = _workspace.Node("echoarg.js", value).WithSecret(secret).WithSecret(string.Empty).WithLogger(logger).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        var logged = string.Join("\n", logger.Messages);
        Assert.DoesNotContain(secret, logged);
        Assert.DoesNotContain(secret, command.ToString());
        Assert.DoesNotContain(secret, result.ToString());
        Assert.Contains("****", logged);
        Assert.Contains("Command (truncated", logged);
        Assert.Contains("StandardOutput (truncated", logged);
        Assert.Contains("StandardError (truncated", logged);
        Assert.Equal("out:" + value, result.StandardOutput);
        Assert.Same(command, result.Command);
    }

    [Fact]
    public void Run_logs_with_a_configured_logger()
    {
        // arrange
        var logger = new RecordingLogger();
        var command = _workspace.Node("echoarg.js", "sync").WithLogger(logger).GetCommand();

        // act
        var result = command.Run();

        // assert
        Assert.True(result.Success);
        Assert.Contains(logger.Messages, x => x.Contains("Result"));
    }

    [Theory]
    [InlineData(0, "Result")]
    [InlineData(0, "StandardOutput")]
    [InlineData(0, "StandardError")]
    [InlineData(7, "Result")]
    [InlineData(7, "StandardOutput")]
    [InlineData(7, "StandardError")]
    public void Run_returns_the_result_when_result_logging_throws(int exitCode, string diagnostic)
    {
        // arrange
        var logger = ThrowingResultLogger(diagnostic);
        var command = _workspace.Node("exit.js", exitCode.ToString()).WithLogger(logger).GetCommand();

        // act
        var result = command.Run(suppressCommandLineExceptions: true);

        // assert
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(exitCode == 0, result.Success);
        Assert.Contains(logger.Messages, x => x.Contains(" " + diagnostic + ":"));
    }

    [Theory]
    [InlineData(0, "Result")]
    [InlineData(0, "StandardOutput")]
    [InlineData(0, "StandardError")]
    [InlineData(7, "Result")]
    [InlineData(7, "StandardOutput")]
    [InlineData(7, "StandardError")]
    public async Task RunAsync_returns_the_result_when_result_logging_throws(int exitCode, string diagnostic)
    {
        // arrange
        var logger = ThrowingResultLogger(diagnostic);
        var command = _workspace.Node("exit.js", exitCode.ToString()).WithLogger(logger).GetCommand();

        // act
        var result = await command.RunAsync(suppressCommandLineExceptions: true);

        // assert
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(exitCode == 0, result.Success);
        Assert.Contains(logger.Messages, x => x.Contains(" " + diagnostic + ":"));
    }

    [Fact]
    public void Run_preserves_unexpected_exit_when_result_logging_throws()
    {
        // arrange
        var command = _workspace.Node("exit.js", "7").WithLogger(ThrowingResultLogger("Result")).GetCommand();

        // act
        var exception = TestWorkspace.Capture<UnexpectedExitCodeException>(() => command.Run());

        // assert
        Assert.Equal(7, exception.Result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_preserves_unexpected_exit_when_result_logging_throws()
    {
        // arrange
        var command = _workspace.Node("exit.js", "7").WithLogger(ThrowingResultLogger("Result")).GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<UnexpectedExitCodeException>(() => command.RunAsync());

        // assert
        Assert.Equal(7, exception.Result.ExitCode);
    }

    [Fact]
    public void Run_preserves_timeout_when_result_logging_throws()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e", "while(true){}")
            .WithTimeout(TimeSpan.FromMilliseconds(200))
            .WithLogger(ThrowingResultLogger("Result"))
            .GetCommand();

        // act
        var exception = TestWorkspace.Capture<ProcessTimeoutException>(() => command.Run());

        // assert
        Assert.True(exception.Result.TimedOut);
        var processId = Assert.IsType<int>(exception.Result.ProcessId);
        Assert.True(TestWorkspace.WaitUntilDead(processId));
    }

    [Fact]
    public async Task RunAsync_preserves_timeout_when_result_logging_throws()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e", "while(true){}")
            .WithTimeout(TimeSpan.FromMilliseconds(200))
            .WithLogger(ThrowingResultLogger("Result"))
            .GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessTimeoutException>(() => command.RunAsync());

        // assert
        Assert.True(exception.Result.TimedOut);
        var processId = Assert.IsType<int>(exception.Result.ProcessId);
        Assert.True(TestWorkspace.WaitUntilDead(processId));
    }

    [Fact]
    public void Run_preserves_cancellation_when_result_logging_throws_and_exceptions_are_suppressed()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e", "while(true){}")
            .WithLogger(ThrowingResultLogger("Result"))
            .GetCommand();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = TestWorkspace.Capture<ProcessCanceledException>(() => command.Run(true, cancellation.Token));

        // assert
        Assert.True(exception.Result.Canceled);
        var processId = Assert.IsType<int>(exception.Result.ProcessId);
        Assert.True(TestWorkspace.WaitUntilDead(processId));
    }

    [Fact]
    public async Task RunAsync_preserves_cancellation_when_result_logging_throws_and_exceptions_are_suppressed()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e", "while(true){}")
            .WithLogger(ThrowingResultLogger("Result"))
            .GetCommand();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessCanceledException>(() => command.RunAsync(true, cancellation.Token));

        // assert
        Assert.True(exception.Result.Canceled);
        var processId = Assert.IsType<int>(exception.Result.ProcessId);
        Assert.True(TestWorkspace.WaitUntilDead(processId));
    }

    [Fact]
    public void LogResult_preserves_a_failed_termination_result_when_logging_throws()
    {
        // arrange
        var command = new CliCommand { Logger = ThrowingResultLogger("Result") };
        var diagnostics = new CommandDiagnostics(command);
        var result = new CliCommandResult { Command = command, TerminationFailed = true, Canceled = true };

        // act
        var exception = Record.Exception(() => diagnostics.LogResult("test", result));

        // assert
        Assert.Null(exception);
        Assert.True(result.TerminationFailed);
        Assert.True(result.Canceled);
    }

    [Fact]
    public void Start_masks_and_truncates_launch_diagnostics_and_retains_a_snapshot()
    {
        // arrange
        var logger = new RecordingLogger();
        var secret = "private-token";
        var command = _workspace.NodeSessionCommand("worker.js", secret + new string('x', 3000));
        command.Secrets.Add(secret);
        command.Logger = logger;
        var expected = command.ToString();

        // act
        using var session = command.Start();
        command.Arguments = "changed";
        session.Terminate();
        var logged = string.Join("\n", logger.Messages);

        // assert
        Assert.Equal(expected, session.Command);
        Assert.DoesNotContain(secret, session.Command);
        Assert.DoesNotContain(secret, logged);
        Assert.Contains("Command (truncated", logged);
        Assert.Contains("Started (truncated", logged);
        Assert.Contains("Termination: EXITED", logged);
        Assert.DoesNotContain("StandardOutput", logged);
        Assert.DoesNotContain("StandardError", logged);
    }

    [Fact]
    public void Start_cleans_up_a_started_process_when_logging_throws()
    {
        // arrange
        var processId = 0;
        var failure = new InvalidOperationException("logger failed");
        var logger = new RecordingLogger
        {
            OnLog = message =>
            {
                if (message.Contains("Started:"))
                {
                    processId = int.Parse(message.Split(' ')[0]);
                    throw failure;
                }
            },
        };
        var command = _workspace.NodeSessionCommand("worker.js");
        command.Logger = logger;

        // act
        var exception = TestWorkspace.Capture<InvalidOperationException>(() => command.Start());

        // assert
        Assert.Same(failure, exception);
        Assert.True(processId > 0);
        Assert.True(TestWorkspace.WaitUntilDead(processId));
        Assert.Contains(logger.Messages, x => x.Contains("Termination: EXITED"));
    }

    [Fact]
    public async Task Terminate_and_Dispose_release_the_tree_when_logging_throws()
    {
        // arrange
        var logger = new RecordingLogger();
        var command = _workspace.NodeSessionCommand("worker.js");
        command.Logger = logger;
        using var session = command.Start();
        await session.StandardInput.WriteLineAsync("spawn");
        var response = await session.StandardOutput.ReadLineAsync();
        logger.OnLog = message => throw new InvalidOperationException("logger failed");

        // act
        var terminateException = Record.Exception(session.Terminate);
        var disposeException = Record.Exception(session.Dispose);

        // assert
        Assert.Equal("spawned", response);
        Assert.Null(terminateException);
        Assert.Null(disposeException);
        Assert.True(TestWorkspace.WaitUntilDead(session.ProcessId));
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")));
    }

    [Fact]
    public async Task Dispose_releases_the_tree_when_logging_throws()
    {
        // arrange
        var logger = new RecordingLogger();
        var command = _workspace.NodeSessionCommand("worker.js");
        command.Logger = logger;
        using var session = command.Start();
        await session.StandardInput.WriteLineAsync("spawn");
        var response = await session.StandardOutput.ReadLineAsync();
        logger.OnLog = message => throw new InvalidOperationException("logger failed");

        // act
        var exception = Record.Exception(session.Dispose);

        // assert
        Assert.Equal("spawned", response);
        Assert.Null(exception);
        Assert.True(TestWorkspace.WaitUntilDead(session.ProcessId));
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")));
    }

    [Fact]
    public async Task RunAsync_and_Start_skip_diagnostics_when_logging_is_disabled()
    {
        // arrange
        var logger = new RecordingLogger { Enabled = false, OnLog = message => throw new InvalidOperationException() };
        var command = _workspace.Node("exit.js", "0").WithLogger(logger).GetCommand();
        var worker = _workspace.NodeSessionCommand("worker.js");
        worker.Logger = logger;

        // act
        var result = await command.RunAsync();
        using var session = worker.Start();
        session.Terminate();
        session.Dispose();

        // assert
        Assert.True(result.Success);
        Assert.Empty(logger.Messages);
    }

    private static RecordingLogger ThrowingResultLogger(string diagnostic)
    {
        return new RecordingLogger
        {
            OnLog = message =>
            {
                if (message.Contains(" " + diagnostic + ":"))
                {
                    throw new InvalidOperationException("logger failed");
                }
            },
        };
    }
}
