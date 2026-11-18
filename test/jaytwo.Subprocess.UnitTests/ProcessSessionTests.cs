using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Streaming;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class ProcessSessionTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Start_defaults_standard_input_new_line_to_lf()
    {
        // arrange
        var command = _workspace.NodeSessionCommand("worker.js");

        // act
        using var session = command.Start();

        // assert
        Assert.Equal("\n", session.StandardInput.NewLine);
    }

    [Fact]
    public async Task Start_supports_repeated_request_response_exchanges_and_exposes_the_process()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        await session.StandardInput.WriteLineAsync("one");
        var first = await session.StandardOutput.ReadLineAsync();
        await session.StandardInput.WriteLineAsync("two");
        var second = await session.StandardOutput.ReadLineAsync();

        // assert
        Assert.Equal("echo:one", first);
        Assert.Equal("echo:two", second);
        Assert.Equal(session.ProcessId, session.Process.Id);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.WaitUntil(() => session.StandardError.Contains("saw:two"), TimeSpan.FromSeconds(20)), "stderr was not drained");
    }

    [Fact]
    public async Task Terminate_kills_the_worker_and_its_descendants_and_unblocks_a_pending_read()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        await session.StandardInput.WriteLineAsync("spawn");
        Assert.Equal("spawned", await session.StandardOutput.ReadLineAsync());
        await session.StandardInput.WriteLineAsync("hang");
        var pendingRead = session.StandardOutput.ReadLineAsync();

        // act
        session.Terminate();
        var line = await pendingRead;

        // assert
        Assert.Null(line);
        Assert.True(session.HasExited);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("worker.pid")));
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")));
    }

    [Fact]
    public async Task Terminate_enforces_a_per_request_timeout_when_wired_to_a_cancellation_token()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        using var requestTimeout = new CancellationTokenSource();
        using var registration = requestTimeout.Token.Register(session.Terminate);

        // act
        await session.StandardInput.WriteLineAsync("hang");
        requestTimeout.CancelAfter(TimeSpan.FromMilliseconds(300));
        var line = await session.StandardOutput.ReadLineAsync();

        // assert
        Assert.Null(line);
        Assert.True(TestWorkspace.WaitUntil(() => session.HasExited, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public async Task Dispose_kills_the_worker_and_its_descendants()
    {
        // arrange
        var session = _workspace.NodeSessionCommand("worker.js").Start();
        await session.StandardInput.WriteLineAsync("spawn");
        Assert.Equal("spawned", await session.StandardOutput.ReadLineAsync());

        // act
        session.Dispose();
        session.Dispose(); // idempotent

        // assert
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("worker.pid")));
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")));
    }

    [Fact]
    public async Task WaitForExitAsync_returns_the_exit_code_and_terminate_is_then_a_no_op()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        // act
        await session.StandardInput.WriteLineAsync("crash");
        var exitCode = await session.WaitForExitAsync();
        session.Terminate();

        // assert
        Assert.Equal(3, exitCode);
        Assert.True(session.HasExited);
    }

    [Fact]
    public async Task WaitForExitAsync_cancellation_abandons_the_wait_without_killing_the_worker()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = await TestWorkspace.CaptureAsync<OperationCanceledException>(() => session.WaitForExitAsync(cancellation.Token));

        // assert
        Assert.NotNull(exception);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.IsAlive(session.ProcessId));
    }

    [Fact]
    public void WaitForExit_returns_true_with_complete_standard_error_once_the_worker_exits()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.WriteLine("crash");

        // act
        var exited = session.WaitForExit(TimeSpan.FromSeconds(20));
        var standardError = session.StandardError;

        // assert
        Assert.True(exited);
        Assert.True(session.HasExited);
        Assert.Contains("saw:crash", standardError);
    }

    [Fact]
    public void WaitForExit_returns_true_after_standard_input_is_closed()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.Dispose();

        // act
        var exited = session.WaitForExit(TimeSpan.FromSeconds(20));

        // assert
        Assert.True(exited);
        Assert.True(session.HasExited);
    }

    [Fact]
    public void WaitForExit_returns_false_without_killing_a_worker_that_is_still_running()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exited = session.WaitForExit(TimeSpan.FromMilliseconds(200));

        // assert
        Assert.False(exited);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.IsAlive(session.ProcessId));
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(-1)] // Timeout.InfiniteTimeSpan
    public void WaitForExit_rejects_a_negative_or_infinite_timeout(int milliseconds)
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exception = TestWorkspace.Capture<ArgumentOutOfRangeException>(() => session.WaitForExit(TimeSpan.FromMilliseconds(milliseconds)));

        // assert
        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public void WaitForExit_throws_after_dispose()
    {
        // arrange
        var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.Dispose();

        // act
        var exception = TestWorkspace.Capture<ObjectDisposedException>(() => session.WaitForExit(TimeSpan.Zero));

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task WaitForExitAsync_with_a_timeout_returns_true_with_complete_standard_error_once_the_worker_exits()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        await session.StandardInput.WriteLineAsync("crash");

        // act
        var exited = await session.WaitForExitAsync(TimeSpan.FromSeconds(20));
        var standardError = session.StandardError;

        // assert
        Assert.True(exited);
        Assert.True(session.HasExited);
        Assert.Contains("saw:crash", standardError);
    }

    [Fact]
    public async Task WaitForExitAsync_with_a_timeout_returns_false_without_killing_a_worker_that_is_still_running()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exited = await session.WaitForExitAsync(TimeSpan.FromMilliseconds(200));

        // assert
        Assert.False(exited);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.IsAlive(session.ProcessId));
    }

    [Fact]
    public async Task WaitForExitAsync_with_a_timeout_cancellation_abandons_the_wait_without_killing_the_worker()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = await TestWorkspace.CaptureAsync<OperationCanceledException>(() => session.WaitForExitAsync(TimeSpan.FromSeconds(20), cancellation.Token));

        // assert
        Assert.NotNull(exception);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.IsAlive(session.ProcessId));
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(-1)] // Timeout.InfiniteTimeSpan
    public async Task WaitForExitAsync_with_a_timeout_rejects_a_negative_or_infinite_timeout(int milliseconds)
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exception = await TestWorkspace.CaptureAsync<ArgumentOutOfRangeException>(() => session.WaitForExitAsync(TimeSpan.FromMilliseconds(milliseconds)));

        // assert
        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public void Shutdown_returns_true_with_complete_standard_error_when_the_worker_exits_on_end_of_input()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.WriteLine("ping");
        Assert.Equal("echo:ping", session.StandardOutput.ReadLine());

        // act
        var exitedOnItsOwn = session.Shutdown(TimeSpan.FromSeconds(20));
        var standardError = session.StandardError;

        // assert
        Assert.True(exitedOnItsOwn);
        Assert.True(session.HasExited);
        Assert.Contains("saw:ping", standardError);
    }

    [Fact]
    public void Shutdown_accepts_standard_input_that_is_already_closed()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.Dispose();

        // act
        var exitedOnItsOwn = session.Shutdown(TimeSpan.FromSeconds(20));

        // assert
        Assert.True(exitedOnItsOwn);
        Assert.True(session.HasExited);
    }

    [Fact]
    public void Shutdown_terminates_a_worker_that_ignores_end_of_input()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("pid.js", "hold").Start();
        Assert.True(_workspace.WaitForFile("pid.pid"));

        // act
        var exitedOnItsOwn = session.Shutdown(TimeSpan.FromMilliseconds(300));

        // assert
        Assert.False(exitedOnItsOwn);
        Assert.True(session.HasExited);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("pid.pid")));
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(-1)] // Timeout.InfiniteTimeSpan
    public void Shutdown_rejects_a_negative_or_infinite_grace_period(int milliseconds)
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exception = TestWorkspace.Capture<ArgumentOutOfRangeException>(() => session.Shutdown(TimeSpan.FromMilliseconds(milliseconds)));

        // assert
        Assert.Equal("gracePeriod", exception.ParamName);
    }

    [Fact]
    public void Shutdown_throws_after_dispose()
    {
        // arrange
        var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.Dispose();

        // act
        var exception = TestWorkspace.Capture<ObjectDisposedException>(() => session.Shutdown(TimeSpan.Zero));

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ShutdownAsync_returns_true_with_complete_standard_error_when_the_worker_exits_on_end_of_input()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        await session.StandardInput.WriteLineAsync("ping");
        Assert.Equal("echo:ping", await session.StandardOutput.ReadLineAsync());

        // act
        var exitedOnItsOwn = await session.ShutdownAsync(TimeSpan.FromSeconds(20));
        var standardError = session.StandardError;

        // assert
        Assert.True(exitedOnItsOwn);
        Assert.True(session.HasExited);
        Assert.Contains("saw:ping", standardError);
    }

    [Fact]
    public async Task ShutdownAsync_terminates_a_worker_that_ignores_end_of_input()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("pid.js", "hold").Start();
        Assert.True(await _workspace.WaitForFileAsync("pid.pid"));

        // act
        var exitedOnItsOwn = await session.ShutdownAsync(TimeSpan.FromMilliseconds(300));

        // assert
        Assert.False(exitedOnItsOwn);
        Assert.True(session.HasExited);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("pid.pid")));
    }

    [Fact]
    public async Task ShutdownAsync_cancellation_terminates_the_worker_and_throws()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("pid.js", "hold").Start();
        Assert.True(await _workspace.WaitForFileAsync("pid.pid"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = await TestWorkspace.CaptureAsync<OperationCanceledException>(() => session.ShutdownAsync(TimeSpan.FromSeconds(20), cancellation.Token));

        // assert
        Assert.NotNull(exception);
        Assert.True(session.HasExited);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("pid.pid")));
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(-1)] // Timeout.InfiniteTimeSpan
    public async Task ShutdownAsync_rejects_a_negative_or_infinite_grace_period(int milliseconds)
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exception = await TestWorkspace.CaptureAsync<ArgumentOutOfRangeException>(() => session.ShutdownAsync(TimeSpan.FromMilliseconds(milliseconds)));

        // assert
        Assert.Equal("gracePeriod", exception.ParamName);
    }

    [Fact]
    public void Start_applies_environment_and_masks_secrets_in_options_text()
    {
        // arrange
        var options = _workspace.NodeSessionCommand("worker.js");
        options.Environment["JAYTWO_TEST"] = "1";
        options.Secrets.Add("worker");

        // act
        var text = options.ToString();
        using var session = options.Start();

        // assert
        Assert.DoesNotContain("worker", text);
        Assert.Contains("****", text);
        Assert.True(session.ProcessId > 0);
    }

    [Fact]
    public void Start_rejects_a_negative_terminate_timeout()
    {
        // arrange
        var options = _workspace.NodeSessionCommand("worker.js");
        options.TerminateTimeout = TimeSpan.FromSeconds(-2);

        // act
        var exception = TestWorkspace.Capture<ArgumentOutOfRangeException>(() => options.Start());

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public void Start_throws_a_launch_failure()
    {
        // arrange
        var options = new CliCommand { FileName = "jaytwo-subprocess-no-such-program" };

        // act
        var exception = TestWorkspace.Capture<Win32Exception>(() => options.Start());

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task Start_accepts_a_built_command()
    {
        // arrange
        var builder = _workspace.NodeWithDirectory("worker.js")
            .WithTimeout(null)
            .WithTerminateTimeout(TimeSpan.FromSeconds(10));

        // act
        using var session = builder.GetCommand().Start();
        await session.StandardInput.WriteLineAsync("ping");
        var response = await session.StandardOutput.ReadLineAsync();

        // assert
        Assert.Equal("echo:ping", response);
        Assert.False(session.HasExited);
    }

    [Fact]
    public async Task Start_accepts_a_builder_configure_action()
    {
        // arrange
        string workerScript = _workspace.PathOf("worker.js");
        string outputDirectory = _workspace.Directory;

        // act
        using var session = CliCommand.Start(b => b
            .WithFileName("node")
            .WithArgument(workerScript)
            .WithArgument(outputDirectory)
            .WithTerminateTimeout(TimeSpan.FromSeconds(10)));
        await session.StandardInput.WriteLineAsync("ping");
        var response = await session.StandardOutput.ReadLineAsync();

        // assert
        Assert.Equal("echo:ping", response);
    }

    [Fact]
    public void Start_from_builder_rejects_one_shot_settings()
    {
        // arrange
        var builder = new CliCommandBuilder("node", _workspace.PathOf("worker.js"))
            .WithTimeout(TimeSpan.FromSeconds(1));

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetCommand().Start());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.Timeout), exception.Message);
    }
}
