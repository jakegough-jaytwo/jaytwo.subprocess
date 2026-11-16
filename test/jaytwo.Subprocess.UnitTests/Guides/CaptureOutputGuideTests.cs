using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// The samples in docs/guides/capture-output.md.
public class CaptureOutputGuideTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task capture_stdout_and_stderr()
    {
        // arrange (no setup needed)

        // act
        // sample: capture-basic
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("console.log('line1'); console.error('err1')")
            .GetCommand();
        CliCommandResult result = await command.ExecuteAsync();

        string stdout = result.StandardOutput ?? string.Empty;
        string stderr = result.StandardError ?? string.Empty;
        // end sample

        // assert
        Assert.Contains("line1", stdout);
        Assert.Contains("err1", stderr);
    }

    [Fact]
    public async Task choose_encodings()
    {
        // arrange
        string encodingScript = _workspace.PathOf("encoding.js");

        // act
        // sample: capture-encodings
        var command = new CliCommandBuilder("node", encodingScript, "utf8", "latin1")
            .WithStandardOutputEncoding(new UTF8Encoding(false))
            .WithStandardErrorEncoding(Encoding.GetEncoding("iso-8859-1"))
            .GetCommand();
        // end sample

        // assert
        CliCommandResult result = await command.ExecuteAsync();
        Assert.True(result.Success);
        Assert.Contains("café", result.StandardOutput);
        Assert.Contains("café", result.StandardError);
    }

    [Fact]
    public async Task bound_an_incomplete_drain()
    {
        // arrange
        string scriptPath = _workspace.PathOf("holdpipes.js");
        string workingDirectory = _workspace.Directory;

        // act
        // sample: capture-incomplete
        var command = new CliCommandBuilder("node", scriptPath, workingDirectory)
            .WithOutputDrainTimeout(TimeSpan.FromSeconds(2))
            .GetCommand();

        CliCommandResult result = await command.ExecuteAsync(suppressCommandLineExceptions: true);

        if (result.OutputIncomplete)
        {
            // treat captured text as a prefix; a descendant may still be alive
        }

        // end sample

        // assert
        Assert.True(result.Success);
        Assert.True(result.OutputIncomplete);
        Assert.Equal("parent-out", result.StandardOutput);
    }
}
