# Timeouts, cancellation and exit codes

Bound how long a run may take, stop it with a `CancellationToken`, and decide which exit codes count as success.

## Expected exit codes

Default is exit code `0`. Accept others when the tool uses them as normal outcomes:

<!-- sample: exit-codes -->
```csharp
var command = new CliCommandBuilder("node", "-e")
    .WithArgument("process.exit(1)")
    .WithExpectedExitCodes(0, 1) // 1 = treated as success for this tool
    .GetCommand();

CliCommandResult result = await command.ExecuteAsync();
```

An unexpected exit code throws `UnexpectedExitCodeException` (`Success == false` on the result).

## Timeout

Default timeout is 60 seconds. Use `Timeout.InfiniteTimeSpan` for no wall-clock limit:

<!-- sample: timeout -->
```csharp
var command = new CliCommandBuilder("node", "-e")
    .WithArgument("console.log('done')")
    .WithTimeout(TimeSpan.FromMinutes(5))
    .GetCommand();

await command.ExecuteAsync();
```

When the timeout elapses the library terminates the **process tree**, waits for exit, drains output (bounded by `OutputDrainTimeout`), and throws `ProcessTimeoutException` (`TimedOut` on the result).

## Cancellation

<!-- sample: cancel -->
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    await command.ExecuteAsync(cts.Token);
}
catch (ProcessCanceledException)
{
    // token canceled after the process started; Result.Canceled is true
}

```

A token that is already canceled before the call throws `OperationCanceledException` and starts nothing. `ProcessCanceledException` derives from `OperationCanceledException`.

## Outcomes at a glance

| Outcome | Thrown | Result flags |
| --- | --- | --- |
| exit code not in `ExpectedExitCodes` | `UnexpectedExitCodeException` | `Success == false` |
| `Timeout` elapsed | `ProcessTimeoutException` | `TimedOut` |
| token canceled after start | `ProcessCanceledException` | `Canceled` |
| token already canceled | `OperationCanceledException` | - |
| terminate attempted but process did not exit within `OutputDrainTimeout` | `ProcessTerminationException` | `TerminationFailed` (plus `TimedOut`/`Canceled`) |

Timeout, unexpected-exit and termination exceptions derive from `SubprocessException` and expose `Result` with both captured streams. `ExitCode` is not meaningful when `TerminationFailed` is set.

## Suppress throwing (except cancel)

<!-- sample: suppress -->
```csharp
CliCommandResult result = await command.ExecuteAsync(suppressCommandLineExceptions: true);

if (result.TimedOut || !result.Success || result.TerminationFailed)
{
    // inspect result.StandardOutput / StandardError yourself
}

```

`suppressCommandLineExceptions: true` returns the failure `Result` instead of throwing for timeout, unexpected exit and termination failure. It never swallows cancellation (`ProcessCanceledException`, or any result with `Canceled` set).

## Things to know

- If the process exits on its own at the same moment a timeout or cancel fires, the natural exit wins; you never get both, and nothing hangs.
- Tree termination uses `Process.Kill(entireProcessTree: true)` on modern .NET; on netstandard2.0/.NET Framework (Windows) it uses `taskkill /T`. Elsewhere without tree kill support, launch refuses with `PlatformNotSupportedException`.
- A descendant orphaned before the kill, or spawned during it, can survive; `OutputDrainTimeout` and `OutputIncomplete` exist for that case. See [Capture output](capture-output.md).
