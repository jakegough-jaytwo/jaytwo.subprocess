using System;
using System.Threading.Tasks;
using jaytwo.RuntimeRevelation;
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
        var command = new CliCommandBuilder()
            .WithRuntimeCondition(x => x.Platform != OSPlatform.Windows, builder =>
            {
                builder.WithFileName("node").WithArguments(new[] { "-e", "console.log('unix')" });
            })
            .WithRuntimeCondition(x => x.Platform == OSPlatform.Windows, builder =>
            {
                builder.WithFileName("node").WithArguments(new[] { "-e", "console.log('windows')" });
            })
            .GetCommand();

        await command.ExecuteAsync();
        // end sample

        // assert
        CliCommandResult result = await command.ExecuteAsync();
        Assert.True(result.Success);
        string expected = RuntimeInformation.Current.Platform == OSPlatform.Windows ? "windows" : "unix";
        Assert.Equal(expected, result.StandardOutput?.Trim());
    }

    [Fact]
    public async Task share_setup_after_the_branch()
    {
        // arrange
        string name = "Ada";
        string projectDirectory = _workspace.Directory;

        // act
        // sample: platform-shared
        var command = new CliCommandBuilder()
            .WithRuntimeCondition(x => x.Platform == OSPlatform.Windows, b => b.WithFileName("node").WithArguments(new[] { "-e", "console.log(process.argv[1])", name }))
            .WithRuntimeCondition(x => x.Platform != OSPlatform.Windows, b => b.WithFileName("node").WithArguments(new[] { "-e", "console.log(process.argv[1])", name }))
            .WithWorkingDirectory(projectDirectory)
            .WithTimeout(TimeSpan.FromMinutes(2))
            .GetCommand();
        // end sample

        // assert
        CliCommandResult result = await command.ExecuteAsync();
        Assert.True(result.Success);
        Assert.Equal(name, result.StandardOutput?.Trim());
    }
}
