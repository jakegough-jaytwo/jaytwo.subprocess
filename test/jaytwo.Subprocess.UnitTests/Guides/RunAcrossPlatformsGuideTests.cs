using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// The samples in docs/guides/run-across-platforms.md.
public class RunAcrossPlatformsGuideTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task branch_on_platform()
    {
        // arrange (no setup needed)

        // act
        // sample: platform-branch
        CliCommandResult result = await new CliCommandBuilder()
            .When(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), b => b.WithFileName("where").WithArgument("node"))
            .When(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows), b => b.WithFileName("which").WithArgument("node"))
            .GetCommand()
            .RunAsync();
        // end sample

        // assert
        Assert.True(result.Success);
        Assert.Contains("node", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task share_setup_after_the_branch()
    {
        // arrange
        string toolName = "node";
        string projectDirectory = _workspace.Directory;

        // act
        // sample: platform-shared
        var command = new CliCommandBuilder()
            .When(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), b => b.WithFileName("where").WithArgument(toolName))
            .When(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows), b => b.WithFileName("which").WithArgument(toolName))
            .WithWorkingDirectory(projectDirectory)
            .WithTimeout(TimeSpan.FromMinutes(2))
            .GetCommand();
        // end sample

        // assert
        CliCommandResult result = await command.RunAsync();
        Assert.True(result.Success);
        Assert.Contains("node", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }
}
