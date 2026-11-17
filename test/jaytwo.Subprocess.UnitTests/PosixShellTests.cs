using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

/// <summary>
/// Probes built only from /bin/sh, sleep and tee, so they run on Linux/macOS without node. Descendants inherit the redirected
/// pipes natively here (no runtime in between), which makes these the most direct checks of pipe-holding behavior.
/// </summary>
public class PosixShellTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [PosixFact]
    public async Task RunAsync_terminates_a_shell_and_its_background_descendant_on_timeout()
    {
        // arrange
        var command = Shell(
            "echo $$ > \"$D/parent.pid\"; sleep 300 & echo $! > \"$D/grandchild.tmp\"; mv \"$D/grandchild.tmp\" \"$D/grandchild.pid\"; " +
            "printf parent-out; printf parent-err >&2; : > \"$D/ready\"; wait")
            .WithTimeout(TimeSpan.FromSeconds(5))
            .GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessTimeoutException>(() => command.RunAsync());

        // assert
        Assert.True(_workspace.WaitForFile("ready"));
        Assert.True(exception.Result.TimedOut);
        Assert.False(exception.Result.OutputIncomplete);
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.Equal("parent-err", exception.Result.StandardError);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("parent.pid")), "shell survived");
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")), "descendant survived");
    }

    [PosixFact]
    public void Run_returns_within_the_drain_bound_when_a_background_descendant_holds_the_pipes_after_a_normal_exit()
    {
        // arrange
        var command = Shell("sleep 300 & echo $! > \"$D/grandchild.pid\"; printf out; printf err >&2; exit 0")
            .WithOutputDrainTimeout(TimeSpan.FromMilliseconds(500))
            .GetCommand();
        var stopwatch = Stopwatch.StartNew();

        // act
        var result = command.Run();
        stopwatch.Stop();

        // assert
        Assert.True(result.Success);
        Assert.True(result.OutputIncomplete);
        Assert.Equal("out", result.StandardOutput);
        Assert.Equal("err", result.StandardError);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
    }

    [PosixFact]
    public async Task RunAsync_bounds_the_wait_when_an_orphaned_descendant_holds_the_pipes_after_timeout()
    {
        // arrange: the subshell exits immediately, orphaning the sleep so tree termination cannot find it
        var command = Shell("(sleep 300 & echo $! > \"$D/grandchild.pid\"); printf parent-out; : > \"$D/ready\"; sleep 300")
            .WithTimeout(TimeSpan.FromSeconds(3))
            .WithOutputDrainTimeout(TimeSpan.FromMilliseconds(500))
            .GetCommand();
        var stopwatch = Stopwatch.StartNew();

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessTimeoutException>(() => command.RunAsync());
        stopwatch.Stop();

        // assert
        Assert.True(_workspace.WaitForFile("ready"));
        Assert.True(exception.Result.TimedOut);
        Assert.True(exception.Result.OutputIncomplete);
        Assert.Equal("parent-out", exception.Result.StandardOutput);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
    }

    [PosixFact]
    public async Task RunAsync_feeds_large_stdin_while_stdout_and_stderr_are_both_active()
    {
        // arrange
        var input = TestWorkspace.Generate(5, 3_000_000);
        var command = new CliCommandBuilder("/bin/sh", "-c", "tee /dev/stderr")
            .WithTimeout(TimeSpan.FromSeconds(60))
            .WithStandardInput(input)
            .GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal(input, result.StandardOutput);
        Assert.Equal(input, result.StandardError);
    }

    [PosixFact]
    public async Task RunAsync_closes_stdin_so_eof_dependent_programs_finish()
    {
        // arrange
        var command = new CliCommandBuilder("/bin/sh", "-c", "cat | wc -c")
            .WithTimeout(TimeSpan.FromSeconds(60))
            .WithStandardInput("12345")
            .GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal("5", result.StandardOutput?.Trim());
    }

    private CliCommandBuilder Shell(string script) =>
        new CliCommandBuilder("/bin/sh", "-c", script)
            .WithTimeout(TimeSpan.FromSeconds(30))
            .WithEnvironmentVariable("D", _workspace.Directory);
}
