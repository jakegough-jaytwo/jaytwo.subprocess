using System;
using System.Text;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class OutputCaptureTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task RunAsync_captures_simultaneous_large_output_on_both_streams()
    {
        // arrange
        var length = 1_000_000; // far beyond any pipe buffer, interleaved so a sequential reader would deadlock
        var command = _workspace.Node("emit.js", length.ToString()).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal(TestWorkspace.Generate(0, length), result.StandardOutput);
        Assert.Equal(TestWorkspace.Generate(7, length), result.StandardError);
        Assert.False(result.OutputIncomplete);
    }

    [Fact]
    public void Run_captures_simultaneous_large_output_on_both_streams()
    {
        // arrange
        var length = 1_000_000;
        var command = _workspace.Node("emit.js", length.ToString()).GetCommand();

        // act
        var result = command.Run();

        // assert
        Assert.Equal(TestWorkspace.Generate(0, length), result.StandardOutput);
        Assert.Equal(TestWorkspace.Generate(7, length), result.StandardError);
    }

    [Fact]
    public async Task RunAsync_preserves_blank_lines_newline_sequences_and_missing_final_newline()
    {
        // arrange
        var command = _workspace.Node("exact.js").GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal("line1\n\nline3\r\n\r\nlast", result.StandardOutput);
        Assert.Equal("e1\r\n\n\nend", result.StandardError);
    }

    [Fact]
    public void Run_preserves_blank_lines_newline_sequences_and_missing_final_newline()
    {
        // arrange
        var command = _workspace.Node("exact.js").GetCommand();

        // act
        var result = command.Run();

        // assert
        Assert.Equal("line1\n\nline3\r\n\r\nlast", result.StandardOutput);
        Assert.Equal("e1\r\n\n\nend", result.StandardError);
    }

    [Theory]
    [InlineData("utf8", "latin1")]
    [InlineData("latin1", "utf8")]
    public async Task RunAsync_decodes_stdout_and_stderr_with_separate_encodings(string stdoutEncoding, string stderrEncoding)
    {
        // arrange
        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var encodings = new System.Collections.Generic.Dictionary<string, Encoding> { ["utf8"] = new UTF8Encoding(false), ["latin1"] = latin1 };
        var latinText = "café ñ ü";
        var unicodeText = "café 日本語 ☃ \U0001F600";

        var command = _workspace.Node("encoding.js", stdoutEncoding, stderrEncoding)
            .WithStandardOutputEncoding(encodings[stdoutEncoding])
            .WithStandardErrorEncoding(encodings[stderrEncoding])
            .GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal(stdoutEncoding == "latin1" ? latinText : unicodeText, result.StandardOutput);
        Assert.Equal(stderrEncoding == "latin1" ? latinText : unicodeText, result.StandardError);
    }

    [Fact]
    public async Task RunAsync_preserves_both_streams_when_exit_code_is_unexpected()
    {
        // arrange
        var command = _workspace.Node("exit.js", "7").GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<UnexpectedExitCodeException>(() => command.RunAsync());

        // assert
        Assert.Equal(7, exception.Result.ExitCode);
        Assert.False(exception.Result.Success);
        Assert.False(exception.Result.TimedOut);
        Assert.False(exception.Result.Canceled);
        Assert.Equal("out-text", exception.Result.StandardOutput);
        Assert.Equal("err-text", exception.Result.StandardError);
    }

    [Fact]
    public void Run_preserves_both_streams_when_exit_code_is_unexpected()
    {
        // arrange
        var command = _workspace.Node("exit.js", "7").GetCommand();

        // act
        var exception = TestWorkspace.Capture<UnexpectedExitCodeException>(() => command.Run());

        // assert
        Assert.Equal(7, exception.Result.ExitCode);
        Assert.Equal("out-text", exception.Result.StandardOutput);
        Assert.Equal("err-text", exception.Result.StandardError);
    }

    [Fact]
    public async Task RunAsync_treats_listed_exit_codes_as_success()
    {
        // arrange
        var command = _workspace.Node("exit.js", "7").WithExpectedExitCodes(0, 7).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.True(result.Success);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_returns_the_failure_result_when_exceptions_are_suppressed()
    {
        // arrange
        var command = _workspace.Node("exit.js", "7").GetCommand();

        // act
        var result = await command.RunAsync(suppressCommandLineExceptions: true);

        // assert
        Assert.False(result.Success);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("out-text", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_passes_argument_edge_cases_through_unchanged()
    {
        // arrange
        var arguments = new[]
        {
            "plain",
            string.Empty,
            " ",
            "with space",
            "with \"quotes\"",
            "single 'quotes'",
            "back\\slash",
            "trailing\\",
            "trailing\\\\",
            "quote\\\"after-backslash",
            "\\\\\"double-backslash-quote",
            "日本語 ☃ café",
            "--flag=value with space",
            "{braces} %PERCENT% $DOLLAR",
        };

        var command = _workspace.Node("argv.js", arguments).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        var expected = arguments.Length + "\n" + string.Join("\n", Array.ConvertAll(arguments, x => TestWorkspace.ToHex(Encoding.UTF8.GetBytes(x))));
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void Run_passes_argument_edge_cases_through_unchanged()
    {
        // arrange
        var arguments = new[] { "plain", string.Empty, "with \"quotes\"", "trailing\\", "日本語" };
        var command = _workspace.Node("argv.js", arguments).GetCommand();

        // act
        var result = command.Run();

        // assert
        var expected = arguments.Length + "\n" + string.Join("\n", Array.ConvertAll(arguments, x => TestWorkspace.ToHex(Encoding.UTF8.GetBytes(x))));
        Assert.Equal(expected, result.StandardOutput);
    }
}
