using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Streaming;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class ProcessSessionTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Start_supports_repeated_request_response_exchanges_and_exposes_the_process()
    {
        // arrange
        using var session = CliProcessSession.Start(_workspace.NodeOptions("worker.js"));
        session.StandardInput.NewLine = "\n";

        // act
        await session.StandardInput.WriteLineAsync("one");
        var first = await session.StandardOutput.ReadLineAsync();
        await session.StandardInput.WriteLineAsync("two");
        var second = await session.StandardOutput.ReadLineAsync();

        // assert
        Assert.Equal("echo:one", first);
        Assert.Equal("echo:two", second);
        Assert.Equal(session.ProcessId, session.Process.Id);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.WaitUntil(() => session.StandardError.Contains("saw:two"), TimeSpan.FromSeconds(20)), "stderr was not drained");
    }

    [Fact]
    public async Task Terminate_kills_the_worker_and_its_descendants_and_unblocks_a_pending_read()
    {
        // arrange
        using var session = CliProcessSession.Start(_workspace.NodeOptions("worker.js"));
        session.StandardInput.NewLine = "\n";
        await session.StandardInput.WriteLineAsync("spawn");
        Assert.Equal("spawned", await session.StandardOutput.ReadLineAsync());
        await session.StandardInput.WriteLineAsync("hang");
        var pendingRead = session.StandardOutput.ReadLineAsync();

        // act
        session.Terminate();
        var line = await pendingRead;

        // assert
        Assert.Null(line);
        Assert.True(session.HasExited);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("worker.pid")));
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")));
    }

    [Fact]
    public async Task Terminate_enforces_a_per_request_timeout_when_wired_to_a_cancellation_token()
    {
        // arrange
        using var session = CliProcessSession.Start(_workspace.NodeOptions("worker.js"));
        session.StandardInput.NewLine = "\n";
        using var requestTimeout = new CancellationTokenSource();
        using var registration = requestTimeout.Token.Register(session.Terminate);

        // act
        await session.StandardInput.WriteLineAsync("hang");
        requestTimeout.CancelAfter(TimeSpan.FromMilliseconds(300));
        var line = await session.StandardOutput.ReadLineAsync();

        // assert
        Assert.Null(line);
        Assert.True(TestWorkspace.WaitUntil(() => session.HasExited, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public async Task Dispose_kills_the_worker_and_its_descendants()
    {
        // arrange
        var session = CliProcessSession.Start(_workspace.NodeOptions("worker.js"));
        session.StandardInput.NewLine = "\n";
        await session.StandardInput.WriteLineAsync("spawn");
        Assert.Equal("spawned", await session.StandardOutput.ReadLineAsync());

        // act
        session.Dispose();
        session.Dispose(); // idempotent

        // assert
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("worker.pid")));
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("grandchild.pid")));
    }

    [Fact]
    public async Task WaitForExitAsync_returns_the_exit_code_and_terminate_is_then_a_no_op()
    {
        // arrange
        using var session = CliProcessSession.Start(_workspace.NodeOptions("worker.js"));
        session.StandardInput.NewLine = "\n";

        // act
        await session.StandardInput.WriteLineAsync("crash");
        var exitCode = await session.WaitForExitAsync();
        session.Terminate();

        // assert
        Assert.Equal(3, exitCode);
        Assert.True(session.HasExited);
    }

    [Fact]
    public async Task WaitForExitAsync_cancellation_abandons_the_wait_without_killing_the_worker()
    {
        // arrange
        using var session = CliProcessSession.Start(_workspace.NodeOptions("worker.js"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = await TestWorkspace.CaptureAsync<OperationCanceledException>(() => session.WaitForExitAsync(cancellation.Token));

        // assert
        Assert.NotNull(exception);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.IsAlive(session.ProcessId));
    }

    [Fact]
    public void Start_applies_environment_and_masks_secrets_in_options_text()
    {
        // arrange
        var options = _workspace.NodeOptions("worker.js");
        options.Environment["JAYTWO_TEST"] = "1";
        options.Secrets.Add("worker");

        // act
        var text = options.ToString();
        using var session = CliProcessSession.Start(options);

        // assert
        Assert.DoesNotContain("worker", text);
        Assert.Contains("****", text);
        Assert.True(session.ProcessId > 0);
    }

    [Fact]
    public void Start_rejects_a_negative_terminate_timeout()
    {
        // arrange
        var options = _workspace.NodeOptions("worker.js");
        options.TerminateTimeout = TimeSpan.FromSeconds(-2);

        // act
        var exception = TestWorkspace.Capture<ArgumentOutOfRangeException>(() => CliProcessSession.Start(options));

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public void Start_throws_a_launch_failure()
    {
        // arrange
        var options = new CliProcessOptions("jaytwo-subprocess-no-such-program");

        // act
        var exception = TestWorkspace.Capture<Win32Exception>(() => CliProcessSession.Start(options));

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task Start_accepts_a_command_builder()
    {
        // arrange
        var builder = _workspace.NodeWithDirectory("worker.js")
            .WithTimeout(null)
            .WithTerminateTimeout(TimeSpan.FromSeconds(10));

        // act
        using var session = CliProcessSession.Start(builder);
        session.StandardInput.NewLine = "\n";
        await session.StandardInput.WriteLineAsync("ping");
        var response = await session.StandardOutput.ReadLineAsync();

        // assert
        Assert.Equal("echo:ping", response);
        Assert.False(session.HasExited);
    }

    [Fact]
    public async Task Start_accepts_a_builder_configure_action()
    {
        // arrange
        string workerScript = _workspace.PathOf("worker.js");
        string outputDirectory = _workspace.Directory;

        // act
        using var session = CliProcessSession.Start(b => b
            .WithFileName("node")
            .WithArgument(workerScript)
            .WithArgument(outputDirectory)
            .WithTerminateTimeout(TimeSpan.FromSeconds(10)));
        session.StandardInput.NewLine = "\n";
        await session.StandardInput.WriteLineAsync("ping");
        var response = await session.StandardOutput.ReadLineAsync();

        // assert
        Assert.Equal("echo:ping", response);
    }

    [Fact]
    public void Start_from_builder_rejects_one_shot_settings()
    {
        // arrange
        var builder = new CliCommandBuilder("node", _workspace.PathOf("worker.js"))
            .WithTimeout(TimeSpan.FromSeconds(1));

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => CliProcessSession.Start(builder));

        // assert
        Assert.Contains(nameof(CliCommandBuilder.Timeout), exception.Message);
    }
}
