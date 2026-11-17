using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// The samples in docs/guides/run-a-command.md.
public class RunACommandGuideTests
{
    [Fact]
    public async Task run_a_command()
    {
        // arrange (no setup needed)

        // act
        // sample: run-basic
        CliCommandResult result = await new CliCommandBuilder("node", "-e")
            .WithFormattedArgument("console.log('{0}')", "hello world")
            .GetCommand()
            .RunAsync();

        Console.WriteLine(result.Command);           // masked ToString of the launch line
        Console.WriteLine(result.ExitCode);
        Console.WriteLine(result.StandardOutput);
        Console.WriteLine(result.StandardError);
        Console.WriteLine(result.Duration);
        Console.WriteLine(result.Success);           // true when the exit code was expected
        // end sample

        // assert
        Assert.True(result.Success);
        Assert.Equal("hello world", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task run_sync_or_async()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("console.log('ok')")
            .GetCommand();

        // act
        // sample: run-sync-async
        CliCommandResult syncResult = command.Run();
        CliCommandResult asyncResult = await command.RunAsync();
        // end sample

        // assert
        Assert.True(syncResult.Success);
        Assert.True(asyncResult.Success);
        Assert.Equal("ok", syncResult.StandardOutput.Trim());
        Assert.Equal("ok", asyncResult.StandardOutput.Trim());
    }

    [Fact]
    public async Task RunAsync_logs_with_a_configured_logger()
    {
        // arrange
        var logger = new RecordingLogger();

        // act
        // sample: run-logging
        CliCommandResult result = await CliCommand.RunAsync(b => b
            .WithFileName("node")
            .WithArgument("-e")
            .WithArgument("console.log('ok')")
            .WithLogger(logger));
        // end sample

        // assert
        Assert.True(result.Success);
        Assert.Contains(logger.Messages, x => x.Contains("Command"));
        Assert.Contains(logger.Messages, x => x.Contains("Result"));
    }
}
