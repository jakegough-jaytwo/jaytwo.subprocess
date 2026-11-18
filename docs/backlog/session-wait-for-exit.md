# Task: bounded `WaitForExit` and graceful `Shutdown` on `CliProcessSession`

Status: Done. The owner asked for this task and approved `Shutdown` on 2026-11-17. It adds four public methods to the Streaming escape hatch. The charter and [scope](../scope.md) ("Process session") were updated the same day to describe them, so do not edit the charter, the scope, or AGENTS.md.

Read [AGENTS.md](../../AGENTS.md) before you start. Its file-editing, code-style, test-naming and guide rules apply to every step below.

## Why

`CliProcessSession` has one way to wait for exit: `Task<int> WaitForExitAsync(CancellationToken)`. It has no timeout parameter and no sync form, and there is no graceful stop. So callers go around the session to the raw `Process`.

The consumer repo `jaytwo.Ergonomics.Pdf` (test project) shows the pattern three times:

```csharp
// ExternalTool.StopSession: close stdin, wait briefly, then kill
session.StandardInput.Dispose();
if (!session.Process.WaitForExit(1000))
{
    session.Terminate();
}

session.Dispose();   // not in a finally: a throw above leaks the session

// DssWorker.Run and PythonProbe: stdout hit EOF (worker died), report its stderr
_session.Process.WaitForExit();   // no bound: hangs forever if the worker closed stdout but is still alive
throw new InvalidOperationException("worker crashed: " + _session.StandardError);
```

The current pattern causes three problems:

1. **There is no bounded wait on the session.** Callers use `Process.WaitForExit(int)` to get one, or `Process.WaitForExit()` and risk an unbounded hang.
2. **`StandardError` may be incomplete after exit.** The session drains stderr on a background task (`_standardErrorPump`). Process exit does not mean that task has read to end of stream, so the crash message can be missing its last lines. No public API waits for the drain.
3. **Graceful stop is hand-rolled.** "Close stdin, wait up to a grace period, then kill the tree" is the normal way to stop a stdin-driven worker. Every consumer writes it themselves, and this one got the cleanup ordering wrong.

`session.Process` for telemetry (`Refresh()`, `PeakWorkingSet64`) is intended use. Leave it alone.

## What to add

Add exactly these four public methods to `src/jaytwo.Subprocess/Streaming/CliProcessSession.cs`, directly below the existing `WaitForExitAsync(CancellationToken)`:

```csharp
public bool WaitForExit(TimeSpan timeout)
public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
public bool Shutdown(TimeSpan gracePeriod)
public Task<bool> ShutdownAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
```

Return `Task<bool>`, not `ValueTask<bool>`, to match the existing `Task<int> WaitForExitAsync`.

### Behavior rules: `WaitForExit` / `WaitForExitAsync`

Each rule below has a test.

1. `timeout` must be `>= TimeSpan.Zero` and at most `int.MaxValue` milliseconds. Anything else, including `Timeout.InfiniteTimeSpan` (which is -1 ms), throws `ArgumentOutOfRangeException` with parameter name `timeout`. Callers who want no bound already have `WaitForExitAsync()`.
2. If the session is disposed, throw `ObjectDisposedException`. Check this after validating `timeout`.
3. Wait up to `timeout` for the process to exit. If it has not exited, return `false`. Do not terminate it.
4. If it exited, also wait for the stderr drain (`_standardErrorPump.Completion`), using only the time left from the same `timeout`. Return `true` whether or not the drain finished in that time. The whole call never takes noticeably longer than `timeout`.
5. `TimeSpan.Zero` is allowed. It returns `true` only if the process has already exited.
6. Async only: if `cancellationToken` fires before the wait completes, throw `OperationCanceledException`. Cancelling abandons the wait and does not kill the process. This is the same rule as the existing `WaitForExitAsync(CancellationToken)`.

### Behavior rules: `Shutdown` / `ShutdownAsync`

7. `gracePeriod` follows rule 1, but the parameter name in the exception is `gracePeriod`. A disposed session throws `ObjectDisposedException` (rule 2).
8. First close `StandardInput` so the worker sees end of input. If the caller already closed it, that is fine. Ignore an `IOException` while closing, because the worker may already be gone.
9. Then wait exactly like `WaitForExit(gracePeriod)`: for the exit, then for the stderr drain, within the grace period. If the process exited, return `true`.
10. If it is still running, call `Terminate()` (tree kill, bounded by `TerminateTimeout`) and return `false`. Never throw because the process will not die; `HasExited` tells the caller whether termination worked.
11. Do not dispose the session. `StandardError`, `HasExited` and `WaitForExitAsync()` stay usable until the caller disposes it.
12. Async only: if `cancellationToken` fires during the grace period, call `Terminate()`, then throw `OperationCanceledException`. This differs from rule 6 on purpose: the caller asked for the worker to stop, and cancelling means "stop waiting politely".

Do not change the existing `WaitForExitAsync(CancellationToken)`, `Terminate` or `Dispose`. `Dispose` stays an immediate tree termination and never waits for a graceful exit.

### Implementation

Use this code. Keep `ConfigureAwait(false)` and the braces on every `if`, because StyleCop requires them. `System.Diagnostics` (for `Stopwatch`) and `System.IO` (for `IOException`) are already imported.

Public methods, directly below the existing `WaitForExitAsync(CancellationToken)`:

```csharp
    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the process to exit and returns whether it did. After an exit it also waits,
    /// within the same timeout, for the stderr drain to finish, so <see cref="StandardError"/> is complete unless a
    /// descendant still holds the pipe. Returning false does not terminate the process; call <see cref="Terminate"/> for that.
    /// </summary>
    public bool WaitForExit(TimeSpan timeout)
    {
        ValidateWaitTimeout(timeout, nameof(timeout));
        ThrowIfDisposed();

        var stopwatch = Stopwatch.StartNew();

        if (!_exited.Task.Wait(timeout))
        {
            return false;
        }

        _standardErrorPump.Completion.Wait(RemainingTime(timeout, stopwatch));
        return true;
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the process to exit and returns whether it did, then waits within the same
    /// timeout for the stderr drain, like <see cref="WaitForExit(TimeSpan)"/>. Cancelling the token only abandons the wait
    /// (throwing <see cref="OperationCanceledException"/>); it does not terminate the process.
    /// </summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateWaitTimeout(timeout, nameof(timeout));
        ThrowIfDisposed();

        var stopwatch = Stopwatch.StartNew();

        if (!await WaitWithTimeoutAsync(_exited.Task, timeout, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await WaitWithTimeoutAsync(_standardErrorPump.Completion, RemainingTime(timeout, stopwatch), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Stops the worker gracefully: closes <see cref="StandardInput"/> so the worker sees end of input, waits up to
    /// <paramref name="gracePeriod"/> like <see cref="WaitForExit(TimeSpan)"/>, and terminates the whole process tree if it is
    /// still running. Returns true if the process exited on its own. Does not dispose the session.
    /// </summary>
    public bool Shutdown(TimeSpan gracePeriod)
    {
        ValidateWaitTimeout(gracePeriod, nameof(gracePeriod));
        ThrowIfDisposed();

        CloseStandardInput();

        if (WaitForExit(gracePeriod))
        {
            return true;
        }

        Terminate();
        return false;
    }

    /// <summary>
    /// The async form of <see cref="Shutdown(TimeSpan)"/>. Cancelling the token cuts the grace period short: the process tree
    /// is terminated and <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    public async Task<bool> ShutdownAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        ValidateWaitTimeout(gracePeriod, nameof(gracePeriod));
        ThrowIfDisposed();

        CloseStandardInput();

        try
        {
            if (await WaitForExitAsync(gracePeriod, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Terminate();
            throw;
        }

        Terminate();
        return false;
    }
```

Private helpers, at the bottom of the class, directly **before** the existing `private void ThrowIfDisposed()`. Keep this order: StyleCop wants the static helpers before the instance ones.

```csharp
    private static void ValidateWaitTimeout(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be non-negative and finite.");
        }
    }

    private static TimeSpan RemainingTime(TimeSpan timeout, Stopwatch stopwatch)
    {
        var remaining = timeout - stopwatch.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    // true when the task finished within the timeout; the linked source releases the delay timer either way
    private static async Task<bool> WaitWithTimeoutAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (task.IsCompleted)
        {
            return true;
        }

        using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout, delayCancellation.Token)).ConfigureAwait(false);
            delayCancellation.Cancel();

            if (completed == task)
            {
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    // the worker may already be gone; a broken pipe must not stop the shutdown
    private void CloseStandardInput()
    {
        try
        {
            StandardInput.Dispose();
        }
        catch (IOException)
        {
        }
    }
```

Facts the code relies on. These are already true, so do not change them:

- `_exited` is a `TaskCompletionSource<bool>` that only ever gets `TrySetResult(true)`, so it never faults.
- `OutputPump.RunAsync` catches every exception, so `Completion` never faults and `Task.Wait` on it cannot throw.
- Disposing a `StreamWriter` twice is a no-op, so `CloseStandardInput` is safe after the caller closed stdin and before `Dispose` closes it again.
- `Terminate()` is idempotent, waits at most `TerminateTimeout`, and never throws for a process that will not die.
- `Task.Delay(TimeSpan, CancellationToken)` and `CancellationTokenSource.CreateLinkedTokenSource` exist on every target (`net10.0;net8.0;net6.0;netstandard2.0`).

## Tests

Add these tests to `test/jaytwo.Subprocess.UnitTests/ProcessSessionTests.cs`, after the existing `WaitForExitAsync_cancellation_abandons_the_wait_without_killing_the_worker` test. **That file uses LF line endings. Keep LF.**

They use two existing `TestWorkspace` fixtures. Start either with `_workspace.NodeSessionCommand(...).Start()`, as the other tests do.

- `worker.js` (`NodeSessionCommand("worker.js")`): every line it reads is logged to stderr as `saw:<line>`. `ping` replies `echo:ping` on stdout. `crash` exits with code 3. Closing stdin makes it exit with code 0. With no input it stays alive.
- `pid.js` with `hold` (`NodeSessionCommand("pid.js", "hold")`): writes its pid to the file `pid.pid` in the workspace and stays alive **without ever reading stdin**, so it ignores end of input. Wait for the file with `_workspace.WaitForFile("pid.pid")`. Do not `ReadLine` its stdout: it writes the pid without a newline.

Sync tests are `public void` and use `StandardInput.WriteLine` / `StandardOutput.ReadLine`. Async tests are `public async Task` and use the `Async` forms. Keep the `// arrange`, `// act`, `// assert` comments. Put the call under test in a variable, never inside an `Assert`.

```csharp
    [Fact]
    public void WaitForExit_returns_true_with_complete_standard_error_once_the_worker_exits()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.WriteLine("crash");

        // act
        var exited = session.WaitForExit(TimeSpan.FromSeconds(20));
        var standardError = session.StandardError;

        // assert
        Assert.True(exited);
        Assert.True(session.HasExited);
        Assert.Contains("saw:crash", standardError);
    }

    [Fact]
    public void WaitForExit_returns_true_after_standard_input_is_closed()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.Dispose();

        // act
        var exited = session.WaitForExit(TimeSpan.FromSeconds(20));

        // assert
        Assert.True(exited);
        Assert.True(session.HasExited);
    }

    [Fact]
    public void WaitForExit_returns_false_without_killing_a_worker_that_is_still_running()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exited = session.WaitForExit(TimeSpan.FromMilliseconds(200));

        // assert
        Assert.False(exited);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.IsAlive(session.ProcessId));
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(-1)] // Timeout.InfiniteTimeSpan
    public void WaitForExit_rejects_a_negative_or_infinite_timeout(int milliseconds)
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exception = TestWorkspace.Capture<ArgumentOutOfRangeException>(() => session.WaitForExit(TimeSpan.FromMilliseconds(milliseconds)));

        // assert
        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public void WaitForExit_throws_after_dispose()
    {
        // arrange
        var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.Dispose();

        // act
        var exception = TestWorkspace.Capture<ObjectDisposedException>(() => session.WaitForExit(TimeSpan.Zero));

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task WaitForExitAsync_with_a_timeout_returns_true_with_complete_standard_error_once_the_worker_exits()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        await session.StandardInput.WriteLineAsync("crash");

        // act
        var exited = await session.WaitForExitAsync(TimeSpan.FromSeconds(20));
        var standardError = session.StandardError;

        // assert
        Assert.True(exited);
        Assert.True(session.HasExited);
        Assert.Contains("saw:crash", standardError);
    }

    [Fact]
    public async Task WaitForExitAsync_with_a_timeout_returns_false_without_killing_a_worker_that_is_still_running()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exited = await session.WaitForExitAsync(TimeSpan.FromMilliseconds(200));

        // assert
        Assert.False(exited);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.IsAlive(session.ProcessId));
    }

    [Fact]
    public async Task WaitForExitAsync_with_a_timeout_cancellation_abandons_the_wait_without_killing_the_worker()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = await TestWorkspace.CaptureAsync<OperationCanceledException>(() => session.WaitForExitAsync(TimeSpan.FromSeconds(20), cancellation.Token));

        // assert
        Assert.NotNull(exception);
        Assert.False(session.HasExited);
        Assert.True(TestWorkspace.IsAlive(session.ProcessId));
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(-1)] // Timeout.InfiniteTimeSpan
    public async Task WaitForExitAsync_with_a_timeout_rejects_a_negative_or_infinite_timeout(int milliseconds)
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exception = await TestWorkspace.CaptureAsync<ArgumentOutOfRangeException>(() => session.WaitForExitAsync(TimeSpan.FromMilliseconds(milliseconds)));

        // assert
        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public void Shutdown_returns_true_with_complete_standard_error_when_the_worker_exits_on_end_of_input()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.WriteLine("ping");
        Assert.Equal("echo:ping", session.StandardOutput.ReadLine());

        // act
        var exitedOnItsOwn = session.Shutdown(TimeSpan.FromSeconds(20));
        var standardError = session.StandardError;

        // assert
        Assert.True(exitedOnItsOwn);
        Assert.True(session.HasExited);
        Assert.Contains("saw:ping", standardError);
    }

    [Fact]
    public void Shutdown_accepts_standard_input_that_is_already_closed()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.StandardInput.Dispose();

        // act
        var exitedOnItsOwn = session.Shutdown(TimeSpan.FromSeconds(20));

        // assert
        Assert.True(exitedOnItsOwn);
        Assert.True(session.HasExited);
    }

    [Fact]
    public void Shutdown_terminates_a_worker_that_ignores_end_of_input()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("pid.js", "hold").Start();
        Assert.True(_workspace.WaitForFile("pid.pid"));

        // act
        var exitedOnItsOwn = session.Shutdown(TimeSpan.FromMilliseconds(300));

        // assert
        Assert.False(exitedOnItsOwn);
        Assert.True(session.HasExited);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("pid.pid")));
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(-1)] // Timeout.InfiniteTimeSpan
    public void Shutdown_rejects_a_negative_or_infinite_grace_period(int milliseconds)
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exception = TestWorkspace.Capture<ArgumentOutOfRangeException>(() => session.Shutdown(TimeSpan.FromMilliseconds(milliseconds)));

        // assert
        Assert.Equal("gracePeriod", exception.ParamName);
    }

    [Fact]
    public void Shutdown_throws_after_dispose()
    {
        // arrange
        var session = _workspace.NodeSessionCommand("worker.js").Start();
        session.Dispose();

        // act
        var exception = TestWorkspace.Capture<ObjectDisposedException>(() => session.Shutdown(TimeSpan.Zero));

        // assert
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ShutdownAsync_returns_true_with_complete_standard_error_when_the_worker_exits_on_end_of_input()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();
        await session.StandardInput.WriteLineAsync("ping");
        Assert.Equal("echo:ping", await session.StandardOutput.ReadLineAsync());

        // act
        var exitedOnItsOwn = await session.ShutdownAsync(TimeSpan.FromSeconds(20));
        var standardError = session.StandardError;

        // assert
        Assert.True(exitedOnItsOwn);
        Assert.True(session.HasExited);
        Assert.Contains("saw:ping", standardError);
    }

    [Fact]
    public async Task ShutdownAsync_terminates_a_worker_that_ignores_end_of_input()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("pid.js", "hold").Start();
        Assert.True(await _workspace.WaitForFileAsync("pid.pid"));

        // act
        var exitedOnItsOwn = await session.ShutdownAsync(TimeSpan.FromMilliseconds(300));

        // assert
        Assert.False(exitedOnItsOwn);
        Assert.True(session.HasExited);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("pid.pid")));
    }

    [Fact]
    public async Task ShutdownAsync_cancellation_terminates_the_worker_and_throws()
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("pid.js", "hold").Start();
        Assert.True(await _workspace.WaitForFileAsync("pid.pid"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // act
        var exception = await TestWorkspace.CaptureAsync<OperationCanceledException>(() => session.ShutdownAsync(TimeSpan.FromSeconds(20), cancellation.Token));

        // assert
        Assert.NotNull(exception);
        Assert.True(session.HasExited);
        Assert.True(TestWorkspace.WaitUntilDead(_workspace.ReadProcessId("pid.pid")));
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(-1)] // Timeout.InfiniteTimeSpan
    public async Task ShutdownAsync_rejects_a_negative_or_infinite_grace_period(int milliseconds)
    {
        // arrange
        using var session = _workspace.NodeSessionCommand("worker.js").Start();

        // act
        var exception = await TestWorkspace.CaptureAsync<ArgumentOutOfRangeException>(() => session.ShutdownAsync(TimeSpan.FromMilliseconds(milliseconds)));

        // assert
        Assert.Equal("gracePeriod", exception.ParamName);
    }
```

`TestWorkspace.Capture` and `CaptureAsync` throw when nothing is thrown, so they never return null.

## Guide update

Change the guide and its test together. Both files use **CRLF** line endings. Keep CRLF.

### `docs/guides/talk-to-a-long-lived-worker.md`

Insert this subsection after the `Dispose()` paragraph in **Lifecycle**, and before the "For workers in Linux containers" paragraph:

````markdown
### Stop a worker gracefully

`Shutdown` closes stdin so the worker sees end of input, waits up to the grace period for it to exit (and for the stderr drain to finish), and terminates the whole tree only if it is still running. It returns `true` when the worker exited on its own.

<!-- sample: session-graceful-stop -->
```csharp
bool exitedOnItsOwn = session.Shutdown(TimeSpan.FromSeconds(5));
string lastWords = session.StandardError;   // complete after a clean exit
```

`Shutdown` does not dispose the session; `using` still releases it. `ShutdownAsync(gracePeriod, token)` is the async form: cancelling the token cuts the grace period short, terminates the tree and throws `OperationCanceledException`.

If your worker stops on something other than end of input (a `quit` command, for example), send it yourself, then call `WaitForExit(timeout)` and `Terminate()` if it returns `false`. `WaitForExit` and `WaitForExitAsync(timeout, token)` never terminate the process on their own.
````

Then in **Things to know**, replace the first bullet with:

```markdown
- `StandardError` is the last 64K characters of stderr, drained continuously so a chatty worker cannot block on a full pipe. It is complete once `WaitForExit(timeout)` or `Shutdown(gracePeriod)` returns `true`, unless a descendant still holds the pipe.
```

### `test/jaytwo.Subprocess.UnitTests/Guides/TalkToALongLivedWorkerGuideTests.cs`

Add this test after `manage_lifecycle`. The lines between `// sample:` and `// end sample` must match the guide fence line for line after trimming.

```csharp
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
```

## Do not

- Do not change `WaitForExitAsync(CancellationToken)`, `Terminate`, `Dispose`, or `OutputPump`. In particular, `Dispose` must not call `Shutdown` or wait for a graceful exit.
- Do not add an `int WaitForExit()` with no timeout, an `ExitCode` property, a builder setting for shutdown, or any other member.
- Do not edit `docs/charter.md`, `docs/scope.md`, `AGENTS.md`, or `README.md`.
- Do not touch the `jaytwo.Ergonomics.Pdf` repo. That is a separate follow-up (below).
- Do not add package references.
- Do not change line endings. If you cannot preserve them with your editor, stop and say so.

## Steps

1. Add the four public methods and four private helpers to `CliProcessSession.cs`.
2. Run `dotnet build` from the repo root. Fix any StyleCop warnings in the new code (there should be none).
3. Add the session tests to `ProcessSessionTests.cs`.
4. Update the guide and add the guide test.
5. Run the tests below. All must pass.

## Verify

`node` must be on `PATH`. From the repo root:

```sh
dotnet test test/jaytwo.Subprocess.UnitTests --filter "FullyQualifiedName~ProcessSessionTests|FullyQualifiedName~Guides"
dotnet test test/jaytwo.Subprocess.UnitTests
dotnet test test/jaytwo.Subprocess.UnitTests.NetStandard
```

On Windows the first project also runs `net48`. `GuideSampleTests` must pass, because it checks that the guide fence and the test region match.

## Done when

- All four methods exist with the signatures above and follow rules 1-12.
- All eighteen new session test methods and `stop_a_worker_gracefully` pass on every test target, and no existing test fails.
- `git diff` touches only `CliProcessSession.cs`, `ProcessSessionTests.cs`, `talk-to-a-long-lived-worker.md`, and `TalkToALongLivedWorkerGuideTests.cs` (plus this task file, if you mark it done).
- `git diff --stat` shows no whole-file line-ending rewrites.

## Follow-up (not part of this task)

After a release with these methods, update `jaytwo.Ergonomics.Pdf/test/jaytwo.Pdf.Tests`:

- `ExternalTool.StopSession`: replace the body with `try { session.Shutdown(TimeSpan.FromSeconds(1)); } finally { session.Dispose(); }` (formatted with braces on their own lines).
- `DssWorker.Run` and `PythonProbe`: use `_session.WaitForExit(TimeSpan.FromSeconds(10))` instead of `_session.Process.WaitForExit()`, then build the crash message from `_session.StandardError`.
- Leave `_session.Process.Refresh()` and `PeakWorkingSet64` as they are.
