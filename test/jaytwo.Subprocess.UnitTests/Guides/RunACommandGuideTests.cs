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
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("console.log('{0}')", "hello world")
            .GetCommand();

        CliCommandResult result = await command.ExecuteAsync();

        Console.WriteLine(result.Command);           // masked ToString of the launch line
        Console.WriteLine(result.ExitCode);
        Console.WriteLine(result.StandardOutput);
        Console.WriteLine(result.StandardError);
        Console.WriteLine(result.Duration);
        Console.WriteLine(result.Success);           // true when the exit code was expected
        // end sample

        // assert
        Assert.True(result.Success);
        Assert.Equal("hello world", result.StandardOutput?.Trim());
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
        CliCommandResult syncResult = command.Execute();
        CliCommandResult asyncResult = await command.ExecuteAsync();
        // end sample

        // assert
        Assert.True(syncResult.Success);
        Assert.True(asyncResult.Success);
        Assert.Equal("ok", syncResult.StandardOutput?.Trim());
        Assert.Equal("ok", asyncResult.StandardOutput?.Trim());
    }

    [Fact]
    public async Task inject_an_executor()
    {
        // arrange
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("console.log('ok')")
            .GetCommand();
        var token = CancellationToken.None;

        // act
        // sample: run-inject-executor
        ICliCommandExecutor executor = new CliCommandExecutor();
        CliCommandResult result = await command.ExecuteAsync(executor);

        // or with a token (requires ICancellableCliCommandExecutor)
        CliCommandResult canceledAware = await command.ExecuteAsync(token, executor);
        // end sample

        // assert
        Assert.True(result.Success);
        Assert.True(canceledAware.Success);
    }
}
