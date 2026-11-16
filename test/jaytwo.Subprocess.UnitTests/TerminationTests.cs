using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class TerminationTests : IDisposable
{
    private static readonly TimeSpan TreeTimeout = TimeSpan.FromSeconds(10); // generous: the parent and descendant must both be up before it fires

    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ExecuteAsync_terminates_the_whole_process_tree_on_timeout_and_keeps_both_streams()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("tree.js").WithTimeout(TreeTimeout).GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessTimeoutException>(() => command.ExecuteAsync());

        // assert
        Assert.True(_workspace.WaitForFile("ready"), "the child never signaled readiness; the timeout may be too short for this machine");
        Assert.True(exception.Result.TimedOut);
        Assert.False(exception.Result.Canceled);
        Assert.False(exception.Result.Success);
        Assert.False(exception.Result.OutputIncomplete);
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.Equal("parent-err", exception.Result.StandardError);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("parent.pid")), "parent survived");
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")), "descendant survived");
    }

    [Fact]
    public void Execute_terminates_the_whole_process_tree_on_timeout_and_keeps_both_streams()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("tree.js").WithTimeout(TreeTimeout).GetCommand();

        // act
        var exception = TestWorkspace.Capture<ProcessTimeoutException>(() => command.Execute());

        // assert
        Assert.True(_workspace.WaitForFile("ready"), "the child never signaled readiness; the timeout may be too short for this machine");
        Assert.True(exception.Result.TimedOut);
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.Equal("parent-err", exception.Result.StandardError);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("parent.pid")), "parent survived");
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")), "descendant survived");
    }

    [Fact]
    public async Task ExecuteAsync_terminates_the_whole_process_tree_on_cancellation_and_reports_it_distinctly()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var command = _workspace.NodeWithDirectory("tree.js").GetCommand(); // 30s timeout: only cancellation can end this
        var execution = TestWorkspace.CaptureAsync<ProcessCanceledException>(() => command.ExecuteAsync(cancellation.Token));

        // act
        Assert.True(_workspace.WaitForFile("ready"));
        cancellation.Cancel();
        var exception = await execution;

        // assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(exception.Result.Canceled);
        Assert.False(exception.Result.TimedOut);
        Assert.False(exception.Result.Success);
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.Equal("parent-err", exception.Result.StandardError);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("parent.pid")), "parent survived");
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")), "descendant survived");
    }

    [Fact]
    public async Task Execute_terminates_the_whole_process_tree_on_cancellation()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var command = _workspace.NodeWithDirectory("tree.js").GetCommand();
        var execution = Task.Run(() => TestWorkspace.Capture<ProcessCanceledException>(() => command.Execute(cancellation.Token)));

        // act
        Assert.True(_workspace.WaitForFile("ready"));
        cancellation.Cancel();
        var exception = await execution;

        // assert
        Assert.True(exception.Result.Canceled);
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("parent.pid")), "parent survived");
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")), "descendant survived");
    }

    [Fact]
    public async Task ExecuteAsync_with_an_explicit_executor_surfaces_cancellation_as_ProcessCanceledException()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var command = _workspace.NodeWithDirectory("tree.js").GetCommand();
        var execution = TestWorkspace.CaptureAsync<OperationCanceledException>(() => command.ExecuteAsync(cancellation.Token, new CliCommandExecutor()));

        // act
        Assert.True(_workspace.WaitForFile("ready"));
        cancellation.Cancel();
        var exception = await execution;

        // assert
        Assert.IsType<ProcessCanceledException>(exception);
    }

    [Fact]
    public async Task ExecuteAsync_throws_without_starting_a_process_when_already_canceled()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("pid.js").GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<OperationCanceledException>(() => command.ExecuteAsync(new CancellationToken(true)));

        // assert
        Assert.False(System.IO.File.Exists(_workspace.PathOf("pid.pid")));
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_swallow_a_cancellation_when_exceptions_are_suppressed()
    {
        // arrange
        var command = new CliCommandBuilder("anything").GetCommand();
        var canceledResult = new CliCommandResult { Command = command, Canceled = true };
        var inner = new Moq.Mock<ICliCommandExecutor>();
        inner.Setup(x => x.ExecuteAsync(command)).Throws(new ProcessCanceledException(canceledResult, new CancellationToken(true)));
        inner.Setup(x => x.Execute(command)).Throws(new ProcessCanceledException(canceledResult, new CancellationToken(true)));

        // act
        var asyncException = await TestWorkspace.CaptureAsync<ProcessCanceledException>(() => inner.Object.ExecuteAsync(command, suppressCommandLineExceptions: true));
        var syncException = TestWorkspace.Capture<ProcessCanceledException>(() => inner.Object.Execute(command, suppressCommandLineExceptions: true));

        // assert
        Assert.Same(canceledResult, asyncException.Result);
        Assert.Same(canceledResult, syncException.Result);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_swallow_a_canceled_subprocess_exception_when_exceptions_are_suppressed()
    {
        // arrange
        var command = new CliCommandBuilder("anything").GetCommand();
        var canceledResult = new CliCommandResult { Command = command, Canceled = true };
        var inner = new Moq.Mock<ICliCommandExecutor>();
        inner.Setup(x => x.ExecuteAsync(command)).Throws(new ProcessTerminationException(canceledResult));

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessTerminationException>(() => inner.Object.ExecuteAsync(command, suppressCommandLineExceptions: true));

        // assert
        Assert.Same(canceledResult, exception.Result);
    }

    [Fact]
    public async Task ExecuteAsync_ignores_cancellation_requested_after_the_process_has_finished()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var command = _workspace.Node("exit.js", "0").GetCommand();

        // act
        var result = await command.ExecuteAsync(cancellation.Token);
        cancellation.Cancel();

        // assert
        Assert.True(result.Success);
        Assert.False(result.Canceled);
    }

    [Fact]
    public async Task ExecuteAsync_bounds_the_wait_when_an_unreachable_descendant_holds_the_pipes_after_timeout()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("escape.js")
            .WithTimeout(TreeTimeout)
            .WithOutputDrainTimeout(TimeSpan.FromMilliseconds(500))
            .GetCommand();
        var stopwatch = Stopwatch.StartNew();

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessTimeoutException>(() => command.ExecuteAsync());
        stopwatch.Stop();

        // assert
        Assert.True(_workspace.WaitForFile("ready"));
        Assert.True(exception.Result.TimedOut);
        Assert.True(exception.Result.OutputIncomplete, "the orphaned descendant still holds the pipes, so EOF can never arrive");
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.Equal("parent-err", exception.Result.StandardError);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
        Assert.True(TestWorkspace.IsAlive(_workspace.ReadProcessId("grandchild.pid")), "the probe expects the escaped descendant to still be running");
    }

    [Fact]
    public async Task ExecuteAsync_bounds_the_wait_when_a_descendant_holds_the_pipes_after_a_normal_exit()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("holdpipes.js")
            .WithOutputDrainTimeout(TimeSpan.FromMilliseconds(500))
            .GetCommand();
        var stopwatch = Stopwatch.StartNew();

        // act
        var result = await command.ExecuteAsync();
        stopwatch.Stop();

        // assert
        Assert.True(result.Success);
        Assert.False(result.TimedOut);
        Assert.True(result.OutputIncomplete);
        Assert.Equal("parent-out", result.StandardOutput);
        Assert.Equal("parent-err", result.StandardError);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void Execute_bounds_the_wait_when_a_descendant_holds_the_pipes_after_a_normal_exit()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("holdpipes.js")
            .WithOutputDrainTimeout(TimeSpan.FromMilliseconds(500))
            .GetCommand();

        // act
        var result = command.Execute();

        // assert
        Assert.True(result.Success);
        Assert.True(result.OutputIncomplete);
        Assert.Equal("parent-out", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_a_negative_timeout()
    {
        // arrange
        var command = _workspace.Node("exit.js", "0").WithTimeout(TimeSpan.FromSeconds(-5)).GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<ArgumentOutOfRangeException>(() => command.ExecuteAsync());

        // assert
        Assert.Equal("Timeout", exception.ParamName);
    }
}
