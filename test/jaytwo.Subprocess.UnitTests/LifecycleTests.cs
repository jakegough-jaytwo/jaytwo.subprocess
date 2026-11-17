using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class LifecycleTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task RunAsync_reports_the_pid_of_the_child_on_the_result()
    {
        // arrange
        var command = _workspace.NodeWithDirectory("pid.js").GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal(result.StandardOutput, result.ProcessId.ToString());
    }

    [Fact]
    public async Task RunAsync_throws_a_launch_failure_without_touching_the_input_stream()
    {
        // arrange
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var command = new CliCommandBuilder("jaytwo-subprocess-no-such-program")
            .WithStandardInput(stream)
            .GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<Win32Exception>(() => command.RunAsync());

        // assert
        Assert.NotNull(exception);
        Assert.True(stream.CanRead);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Run_throws_a_launch_failure()
    {
        // arrange
        var command = new CliCommandBuilder("jaytwo-subprocess-no-such-program").GetCommand();

        // act
        var exception = TestWorkspace.Capture<Win32Exception>(() => command.Run());

        // assert
        Assert.NotNull(exception);
    }
}
