using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Streaming;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// The samples in docs/guides/talk-to-a-long-lived-worker.md.
public class TalkToALongLivedWorkerGuideTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task talk_to_a_worker()
    {
        // arrange
        string workerScript = _workspace.PathOf("worker.js");
        string outputDirectory = _workspace.Directory;
        string request = "ping";

        // act
        // sample: session-basic
        using var session = new CliCommandBuilder("node", workerScript, outputDirectory).GetCommand().Start();

        await session.StandardInput.WriteLineAsync(request);
        string? response = await session.StandardOutput.ReadLineAsync();   // null once the worker is gone
        // end sample

        // assert
        Assert.Equal("echo:ping", response);
    }

    [Fact]
    public async Task enforce_a_per_request_timeout()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        await session.StandardInput.WriteLineAsync("hang");
        var requestTimeout = TimeSpan.FromMilliseconds(300);

        // act
        // sample: session-timeout
        using var timeout = new CancellationTokenSource(requestTimeout);
        using var registration = timeout.Token.Register(session.Terminate);

        string? response = await session.StandardOutput.ReadLineAsync();
        // end sample

        // assert
        Assert.Null(response);
        Assert.True(TestWorkspace.WaitUntil(() => session.HasExited, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public async Task manage_lifecycle()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        var token = CancellationToken.None;

        // act
        // sample: session-lifecycle
        int pid = session.ProcessId;           // telemetry
        Process process = session.Process;     // borrowed; do not dispose or use its redirected streams

        session.Terminate();                   // kill the tree; idempotent; check HasExited if needed
        int exitCode = await session.WaitForExitAsync(token);   // cancel abandons the wait without killing
        // end sample

        // assert
        Assert.Equal(pid, process.Id);
        Assert.True(session.HasExited);
        Assert.True(exitCode != int.MinValue); // any recorded exit code is fine
    }

    [Fact]
    public void stop_a_worker_gracefully()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.WriteLine("ping");
        Assert.Equal("echo:ping", session.StandardOutput.ReadLine());

        // act
        // sample: session-graceful-stop
        bool exitedOnItsOwn = session.Shutdown(TimeSpan.FromSeconds(5));
        string lastWords = session.StandardError;   // complete after a clean exit
        // end sample

        // assert
        Assert.True(exitedOnItsOwn);
        Assert.Contains("saw:ping", lastWords);
    }

    [Fact]
    public void configure_session_options()
    {
        // arrange
        string workerScript = _workspace.PathOf("worker.js");
        string outputDirectory = _workspace.Directory;
        string workerDirectory = _workspace.Directory;

        // act
        // sample: session-options
        using var session = new CliCommandBuilder("node", workerScript, outputDirectory)
                .WithWorkingDirectory(workerDirectory)
                .WithEnvironmentVariable("NODE_ENV", "test")
                .WithTerminateTimeout(TimeSpan.FromSeconds(10)).GetCommand().Start();
        // end sample

        // assert
        Assert.False(session.HasExited);
    }
}
