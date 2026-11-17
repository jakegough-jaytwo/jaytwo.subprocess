using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// The samples in docs/guides/feed-standard-input.md.
public class FeedStandardInputGuideTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task feed_text_on_standard_input()
    {
        // arrange
        string echoScript = _workspace.PathOf("echo.js");

        // act
        // sample: stdin-text
        var command = new CliCommandBuilder("node", echoScript)
            .WithStandardInput("hello")
            .GetCommand();

        CliCommandResult result = await command.RunAsync();
        Console.WriteLine(result.StandardOutput);
        // end sample

        // assert
        Assert.Equal("hello<EOF>", result.StandardOutput);
    }

    [Fact]
    public async Task feed_bytes_from_a_stream()
    {
        // arrange
        string inputPath = _workspace.PathOf("payload.bin");
        File.WriteAllBytes(inputPath, new byte[] { 0x01, 0x02, 0xFF });
        string hexScript = _workspace.PathOf("hex.js");

        // act
        // sample: stdin-stream
        using FileStream inputStream = File.OpenRead(inputPath);

        var command = new CliCommandBuilder("node", hexScript)
            .WithStandardInput(inputStream)
            .GetCommand();

        CliCommandResult result = await command.RunAsync();
        // end sample

        // assert
        Assert.Equal("0102ff", result.StandardOutput);
    }
}
