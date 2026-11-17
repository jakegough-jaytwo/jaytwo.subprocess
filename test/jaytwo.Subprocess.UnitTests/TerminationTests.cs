using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class TerminationTests : IDisposable
{
    private static readonly TimeSpan TreeTimeout = TimeSpan.FromSeconds(3); // allow both processes to start without making every timeout probe wait ten seconds

    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task RunAsync_terminates_the_whole_process_tree_on_timeout_and_keeps_both_streams()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("tree.js").WithTimeout(TreeTimeout).GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessTimeoutException>(() => command.RunAsync());

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
    public void Run_terminates_the_whole_process_tree_on_timeout_and_keeps_both_streams()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("tree.js").WithTimeout(TreeTimeout).GetCommand();

        // act
        var exception = TestWorkspace.Capture<ProcessTimeoutException>(() => command.Run());

        // assert
        Assert.True(_workspace.WaitForFile("ready"), "the child never signaled readiness; the timeout may be too short for this machine");
        Assert.True(exception.Result.TimedOut);
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.Equal("parent-err", exception.Result.StandardError);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("parent.pid")), "parent survived");
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")), "descendant survived");
    }

    [Fact]
    public async Task RunAsync_terminates_the_whole_process_tree_on_cancellation_and_reports_it_distinctly()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var command = _workspace.NodeWithDirectory("tree.js").GetCommand(); // 30s timeout: only cancellation can end this
        var execution = TestWorkspace.CaptureAsync<ProcessCanceledException>(() => command.RunAsync(cancellation.Token));

        // act
        var ready = await _workspace.WaitForFileAsync("ready");
        cancellation.Cancel();
        var exception = await execution;

        // assert
        Assert.True(ready);
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
    public async Task Run_terminates_the_whole_process_tree_on_cancellation()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var command = _workspace.NodeWithDirectory("tree.js").GetCommand();
        var execution = Task.Run(() => TestWorkspace.Capture<ProcessCanceledException>(() => command.Run(cancellation.Token)));

        // act
        var ready = await _workspace.WaitForFileAsync("ready");
        cancellation.Cancel();
        var exception = await execution;

        // assert
        Assert.True(ready);
        Assert.True(exception.Result.Canceled);
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("parent.pid")), "parent survived");
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")), "descendant survived");
    }

    [Fact]
    public async Task RunAsync_throws_without_starting_a_process_when_already_canceled()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("pid.js").GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<OperationCanceledException>(() => command.RunAsync(new CancellationToken(true)));

        // assert
        Assert.False(System.IO.File.Exists(_workspace.PathOf("pid.pid")));
        Assert.NotNull(exception);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void ShouldSuppress_preserves_cancellation_when_termination_fails(bool canceled, bool suppress, bool expected)
    {
        // arrange
        var result = new CliCommandResult { Canceled = canceled, TerminationFailed = true };
        var exception = new ProcessTerminationException(result);

        // act
        var actual = CliCommand.ShouldSuppress(exception, suppress);

        // assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task RunAsync_ignores_cancellation_requested_after_the_process_has_finished()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var command = _workspace.Node("exit.js", "0").GetCommand();

        // act
        var result = await command.RunAsync(cancellation.Token);
        cancellation.Cancel();

        // assert
        Assert.True(result.Success);
        Assert.False(result.Canceled);
    }

    [Fact]
    public async Task RunAsync_bounds_the_wait_when_an_unreachable_descendant_holds_the_pipes_after_timeout()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("escape.js")
            .WithTimeout(TreeTimeout)
            .WithOutputDrainTimeout(TimeSpan.FromMilliseconds(500))
            .GetCommand();
        var stopwatch = Stopwatch.StartNew();

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessTimeoutException>(() => command.RunAsync());
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
    public async Task RunAsync_bounds_the_wait_when_a_descendant_holds_the_pipes_after_a_normal_exit()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("holdpipes.js")
            .WithOutputDrainTimeout(TimeSpan.FromMilliseconds(500))
            .GetCommand();
        var stopwatch = Stopwatch.StartNew();

        // act
        var result = await command.RunAsync();
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
    public void Run_bounds_the_wait_when_a_descendant_holds_the_pipes_after_a_normal_exit()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("holdpipes.js")
            .WithOutputDrainTimeout(TimeSpan.FromMilliseconds(500))
            .GetCommand();

        // act
        var result = command.Run();

        // assert
        Assert.True(result.Success);
        Assert.True(result.OutputIncomplete);
        Assert.Equal("parent-out", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_rejects_a_negative_timeout()
    {
        // arrange
        var command = _workspace.Node("exit.js", "0").WithTimeout(TimeSpan.FromSeconds(-5)).GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<ArgumentOutOfRangeException>(() => command.RunAsync());

        // assert
        Assert.Equal("Timeout", exception.ParamName);
    }
}
