using System;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// The samples in docs/guides/timeouts-cancellation-and-exit-codes.md.
public class TimeoutsCancellationAndExitCodesGuideTests
{
    [Fact]
    public async Task accept_expected_exit_codes()
    {
        // arrange (no setup needed)

        // act
        // sample: exit-codes
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("process.exit(1)")
            .WithExpectedExitCodes(0, 1) // 1 = treated as success for this tool
            .GetCommand();

        CliCommandResult result = await command.ExecuteAsync();
        // end sample

        // assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task set_a_timeout()
    {
        // arrange (no setup needed)

        // act
        // sample: timeout
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("console.log('done')")
            .WithTimeout(TimeSpan.FromMinutes(5))
            .GetCommand();

        await command.ExecuteAsync();
        // end sample

        // assert
        CliCommandResult result = await command.ExecuteAsync();
        Assert.True(result.Success);
        Assert.Equal("done", result.StandardOutput?.Trim());
    }

    [Fact]
    public async Task cancel_a_run()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("console.log('done')")
            .GetCommand();

        // act
        // sample: cancel
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await command.ExecuteAsync(cts.Token);
        }
        catch (ProcessCanceledException)
        {
            // token canceled after the process started; Result.Canceled is true
        }

        // end sample

        // assert
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task suppress_command_line_exceptions()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("process.exit(7)")
            .GetCommand();

        // act
        // sample: suppress
        CliCommandResult result = await command.ExecuteAsync(suppressCommandLineExceptions: true);

        if (result.TimedOut || !result.Success || result.TerminationFailed)
        {
            // inspect result.StandardOutput / StandardError yourself
        }

        // end sample

        // assert
        Assert.False(result.Success);
        Assert.Equal(7, result.ExitCode);
    }
}
