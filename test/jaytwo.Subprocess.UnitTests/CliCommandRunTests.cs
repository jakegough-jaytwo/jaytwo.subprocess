using System;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class CliCommandRunTests
{
    [Fact]
    public async Task RunAsync_runs_a_materialized_command()
    {
        // arrange
        var builder = new CliCommandBuilder("node", "-e")
            .WithFormattedArgument("console.log('{0}')", "from-builder");

        // act
        var result = await builder.GetCommand().RunAsync();

        // assert
        Assert.True(result.Success);
        Assert.Equal("from-builder", result.StandardOutput.Trim());
    }

    [Fact]
    public void Run_runs_a_materialized_command_synchronously()
    {
        // arrange
        var builder = new CliCommandBuilder("node", "-e")
            .WithFormattedArgument("console.log('{0}')", "sync-builder");

        // act
        var result = builder.GetCommand().Run();

        // assert
        Assert.True(result.Success);
        Assert.Equal("sync-builder", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task RunAsync_suppress_and_cancel_returns_result_for_unexpected_exit()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("process.exit(7)")
            .GetCommand();

        // act
        var result = await command.RunAsync(suppressCommandLineExceptions: true, CancellationToken.None);

        // assert
        Assert.False(result.Success);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_suppress_and_cancel_still_throws_when_canceled()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("while(true){}")
            .WithTimeout(Timeout.InfiniteTimeSpan)
            .GetCommand();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = await TestWorkspace.CaptureAsync<ProcessCanceledException>(
            () => command.RunAsync(suppressCommandLineExceptions: true, cts.Token));

        // assert
        Assert.NotNull(exception);
        Assert.True(exception.Result.Canceled);
    }

    [Fact]
    public void Build_preserves_unset_values_and_Start_accepts_a_default_command()
    {
        // arrange
        var command = CliCommand.Build(b => b.WithFileName("node").WithArgument("-e").WithArgument("process.stdin.resume()"));

        // act
        using var session = command.Start();

        // assert
        Assert.Null(command.Timeout);
        Assert.Null(command.OutputDrainTimeout);
        Assert.Null(command.TerminateTimeout);
        Assert.Empty(command.ExpectedExitCodes);
        Assert.Null(command.StandardInput);
        Assert.Null(command.StandardInputStream);
        Assert.True(session.ProcessId > 0);
    }

    [Fact]
    public void Run_configures_and_runs_with_each_overload()
    {
        // arrange
        Action<CliCommandBuilder> configure = b => b.WithFileName("node").WithArgument("-e").WithArgument("console.log('static')");

        // act
        var basic = CliCommand.Run(configure);
        var suppressed = CliCommand.Run(configure, true);
        var cancellable = CliCommand.Run(configure, CancellationToken.None);
        var combined = CliCommand.Run(configure, true, CancellationToken.None);

        // assert
        Assert.Equal("static", basic.StandardOutput.Trim());
        Assert.True(suppressed.Success);
        Assert.True(cancellable.Success);
        Assert.True(combined.Success);
    }

    [Fact]
    public async Task RunAsync_configures_and_runs_with_each_overload()
    {
        // arrange
        Action<CliCommandBuilder> configure = b => b.WithFileName("node").WithArgument("-e").WithArgument("console.log('static')");

        // act
        var basic = await CliCommand.RunAsync(configure);
        var suppressed = await CliCommand.RunAsync(configure, true);
        var cancellable = await CliCommand.RunAsync(configure, CancellationToken.None);
        var combined = await CliCommand.RunAsync(configure, true, CancellationToken.None);

        // assert
        Assert.Equal("static", basic.StandardOutput.Trim());
        Assert.True(suppressed.Success);
        Assert.True(cancellable.Success);
        Assert.True(combined.Success);
    }

    [Fact]
    public void Run_suppression_still_throws_when_canceled()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e", "while(true){}").GetCommand();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = TestWorkspace.Capture<ProcessCanceledException>(() => command.Run(true, cancellation.Token));

        // assert
        Assert.True(exception.Result.Canceled);
    }

    [Fact]
    public async Task RunAsync_rejects_terminate_timeout_and_runs_after_it_is_cleared()
    {
        // arrange
        var command = CliCommand.Build(b => b.WithFileName("node").WithArgument("-e").WithArgument("process.exit(0)").WithTerminateTimeout(TimeSpan.Zero));

        // act
        var exception = await TestWorkspace.CaptureAsync<InvalidOperationException>(() => command.RunAsync());
        command.TerminateTimeout = null;
        var result = await command.RunAsync();

        // assert
        Assert.Contains(nameof(CliCommand.TerminateTimeout), exception.Message);
        Assert.True(result.Success);
    }

    [Fact]
    public void Run_succeeds_after_terminate_timeout_is_cleared()
    {
        // arrange
        var command = CliCommand.Build(b => b.WithFileName("node").WithArgument("-e").WithArgument("process.exit(0)").WithTerminateTimeout(TimeSpan.Zero));
        command.TerminateTimeout = null;

        // act
        var result = command.Run();

        // assert
        Assert.True(result.Success);
    }

    [Fact]
    public void Start_accepts_cleared_settings_on_an_already_built_command()
    {
        // arrange
        using var input = new System.IO.MemoryStream();
        var command = CliCommand.Build(b => b.WithFileName("node").WithArgument("-e").WithArgument("process.stdin.resume()")
            .WithTimeout(TimeSpan.Zero).WithExpectedExitCode(1).WithStandardInput(input).WithOutputDrainTimeout(TimeSpan.Zero));
        command.StandardInput = null;
        command.StandardInputStream = null;
        command.Timeout = null;
        command.ExpectedExitCodes = null!;
        command.OutputDrainTimeout = null;

        // act
        using var session = command.Start();

        // assert
        Assert.True(session.ProcessId > 0);
        Assert.True(input.CanRead);
        Assert.Equal(0, input.Position);
    }

    [Fact]
    public void Build_rejects_a_null_configure_action()
    {
        // arrange (no setup needed)

        // act
        var exception = TestWorkspace.Capture<ArgumentNullException>(() => CliCommand.Build(null!));

        // assert
        Assert.Equal("configure", exception.ParamName);
    }
}
