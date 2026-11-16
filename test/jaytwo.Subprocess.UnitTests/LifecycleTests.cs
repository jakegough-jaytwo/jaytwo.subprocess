using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class LifecycleTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task ExecuteAsync_reports_the_pid_of_the_child_on_the_result()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("pid.js").GetCommand();

        // act
        var result = await command.ExecuteAsync();

        // assert
        Assert.Equal(result.StandardOutput, result.ProcessId.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_throws_a_launch_failure_without_touching_the_input_stream()
    {
        // arrange
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var command = new CliCommandBuilder("jaytwo-subprocess-no-such-program")
            .WithStandardInput(stream)
            .GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<Win32Exception>(() => command.ExecuteAsync());

        // assert
        Assert.NotNull(exception);
        Assert.True(stream.CanRead);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Execute_throws_a_launch_failure()
    {
        // arrange
        var command = new CliCommandBuilder("jaytwo-subprocess-no-such-program").GetCommand();

        // act
        var exception = TestWorkspace.Capture<Win32Exception>(() => command.Execute());

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ExecuteAsync_masks_secrets_in_logs_and_diagnostics()
    {
        // arrange
        var secret = "rosebudwasthesled";
        var logger = new Mock<ILogger<ICliCommandExecutor>>();
        var command = _workspace.Node("echoarg.js", secret).WithSecret(secret).WithSecret(string.Empty).GetCommand();

        // act
        var result = await new CliCommandExecutor(logger.Object).ExecuteAsync(command);

        // assert
        var logged = string.Join("\n", logger.Invocations.Where(x => x.Method.Name == nameof(ILogger.Log)).Select(x => x.Arguments[2]?.ToString()));
        Assert.Contains("out:****", logged);
        Assert.Contains("err:****", logged);
        Assert.DoesNotContain(secret, logged);
        Assert.DoesNotContain(secret, result.ToString());
        Assert.DoesNotContain(secret, command.ToString());
        Assert.Equal("out:" + secret, result.StandardOutput); // the result carries the real output; only diagnostics are masked
    }

    [Fact]
    public async Task ExecuteAsync_with_a_token_rejects_executors_that_cannot_honor_it()
    {
        // arrange
        var executor = new Mock<ICliCommandExecutor>().Object;
        var command = _workspace.Node("exit.js", "0").GetCommand();
        using var cancellation = new CancellationTokenSource();

        // act
        var exception = await TestWorkspace.CaptureAsync<NotSupportedException>(() => executor.ExecuteAsync(command, cancellation.Token));

        // assert
        Assert.Contains(nameof(ICancellableCliCommandExecutor), exception.Message);
    }
}
