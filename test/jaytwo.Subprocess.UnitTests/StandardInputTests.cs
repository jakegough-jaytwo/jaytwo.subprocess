using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class StandardInputTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task RunAsync_feeds_large_stdin_while_both_output_streams_are_active()
    {
        // arrange
        var input = TestWorkspace.Generate(3, 3_000_000); // the child echoes each chunk to stdout and stderr, so a write-then-read implementation deadlocks
        var command = _workspace.Node("echo.js")
            .WithStandardInput(input)
            .WithStandardOutputEncoding(new UTF8Encoding(false))
            .WithStandardErrorEncoding(new UTF8Encoding(false))
            .GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal(input + "<EOF>", result.StandardOutput);
        Assert.Equal(input, result.StandardError);
    }

    [Fact]
    public void Run_feeds_large_stdin_while_both_output_streams_are_active()
    {
        // arrange
        var input = TestWorkspace.Generate(3, 3_000_000);
        var command = _workspace.Node("echo.js").WithStandardInput(input).GetCommand();

        // act
        var result = command.Run();

        // assert
        Assert.Equal(input + "<EOF>", result.StandardOutput);
        Assert.Equal(input, result.StandardError);
    }

    [Fact]
    public async Task RunAsync_closes_stdin_after_empty_text_so_eof_dependent_children_exit()
    {
        // arrange
        var command = _workspace.Node("hex.js").WithStandardInput(string.Empty).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_encodes_stdin_text_as_utf8_without_a_byte_order_mark_by_default()
    {
        // arrange
        var text = "héllo 日本語";
        var command = _workspace.Node("hex.js").WithStandardInput(text).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal(TestWorkspace.ToHex(Encoding.UTF8.GetBytes(text)), result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_encodes_stdin_text_with_the_requested_encoding()
    {
        // arrange
        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var command = _workspace.Node("hex.js").WithStandardInput("café", latin1).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal("636166e9", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_copies_stdin_stream_from_its_current_position_and_leaves_it_open()
    {
        // arrange
        var stream = new MemoryStream(new byte[] { 9, 9, 0, 1, 2, 254, 255 });
        stream.Position = 2;
        var command = _workspace.Node("hex.js").WithStandardInput(stream).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.Equal("000102feff", result.StandardOutput);
        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void Run_copies_stdin_stream_and_leaves_it_open()
    {
        // arrange
        var stream = new MemoryStream(new byte[] { 0, 1, 2, 254, 255 });
        var command = _workspace.Node("hex.js").WithStandardInput(stream).GetCommand();

        // act
        var result = command.Run();

        // assert
        Assert.Equal("000102feff", result.StandardOutput);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task RunAsync_succeeds_when_the_child_exits_before_reading_large_stdin()
    {
        // arrange
        var input = TestWorkspace.Generate(0, 8_000_000);
        var command = _workspace.Node("exit.js", "0").WithStandardInput(input).GetCommand();

        // act
        var result = await command.RunAsync();

        // assert
        Assert.True(result.Success);
        Assert.Equal("out-text", result.StandardOutput);
    }

    [Fact]
    public void Run_succeeds_when_the_child_exits_before_reading_large_stdin()
    {
        // arrange
        var input = TestWorkspace.Generate(0, 8_000_000);
        var command = _workspace.Node("exit.js", "0").WithStandardInput(input).GetCommand();

        // act
        var result = command.Run();

        // assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RunAsync_rejects_text_and_stream_stdin_together()
    {
        // arrange
        var command = _workspace.Node("hex.js").GetCommand();
        command.StandardInput = "text";
        command.StandardInputStream = new MemoryStream();

        // act
        var exception = await TestWorkspace.CaptureAsync<ArgumentException>(() => command.RunAsync());

        // assert
        Assert.Contains("not both", exception.Message);
    }

    [Fact]
    public async Task RunAsync_terminates_the_child_and_rethrows_when_the_stdin_stream_fails()
    {
        // arrange
        var stream = new ThrowingStream(() => _workspace.WaitForFile("pid.pid")); // fail only once the child is known to be running
        var command = _workspace.NodeWithDirectory("pid.js", "hold")
            .WithStandardInput(stream)
            .GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<IOException>(() => command.RunAsync());

        // assert
        Assert.Equal("stream-failure", exception.Message);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("pid.pid")));
    }

    [Fact]
    public async Task RunAsync_times_out_while_stdin_is_blocked_because_the_child_never_reads()
    {
        // arrange
        var input = TestWorkspace.Generate(0, 4_000_000); // larger than the pipe buffer, so the writer is blocked
        var command = _workspace.NodeWithDirectory("pid.js", "hold")
            .WithStandardInput(input)
            .WithTimeout(TimeSpan.FromSeconds(3))
            .WithOutputDrainTimeout(TimeSpan.FromSeconds(5))
            .GetCommand();

        // act
        var exception = await TestWorkspace.CaptureAsync<Exceptions.ProcessTimeoutException>(() => command.RunAsync());

        // assert
        Assert.True(exception.Result.TimedOut);
        Assert.False(exception.Result.OutputIncomplete);
        Assert.True(TestWorkspace.WaitUntilDead(exception.Result.ProcessId!.Value));
    }

    private sealed class ThrowingStream : Stream
    {
        private readonly Func<bool> _beforeFailing;

        public ThrowingStream(Func<bool> beforeFailing) => _beforeFailing = beforeFailing;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _beforeFailing();
            throw new IOException("stream-failure");
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
