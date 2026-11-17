# jaytwo.Subprocess

<p align="center">
  <a href="https://jenkins.jaytwo.com/job/jaytwo.Subprocess/job/master/" alt="Build Status (master)">
    <img src="https://jenkins.jaytwo.com/buildStatus/icon?job=jaytwo.Subprocess%2Fmaster&subject=build%20(master)" /></a>
  <a href="https://jenkins.jaytwo.com/job/jaytwo.Subprocess/job/develop/" alt="Build Status (develop)">
    <img src="https://jenkins.jaytwo.com/buildStatus/icon?job=jaytwo.Subprocess%2Fdevelop&subject=build%20(develop)" /></a>
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/jaytwo.Subprocess/" alt="NuGet Package jaytwo.Subprocess">
    <img src="https://img.shields.io/nuget/v/jaytwo.Subprocess.svg?logo=nuget&label=jaytwo.Subprocess" /></a>
  <a href="https://www.nuget.org/packages/jaytwo.Subprocess/" alt="NuGet Package jaytwo.Subprocess (beta)">
    <img src="https://img.shields.io/nuget/vpre/jaytwo.Subprocess.svg?logo=nuget&label=jaytwo.Subprocess" /></a>
</p>

A simplified way to invoke external processes in .NET.

Run a CLI, get decoded stdout/stderr and a clear outcome (exit code, timeout, or cancel). The hard parts (draining both streams, killing the whole process tree on timeout or cancel, argument quoting, secrets masking) are handled for you. It is deliberately small: not a general-purpose Process framework and not a CliWrap competitor.

## Installation

Add the NuGet package

```
PM> Install-Package jaytwo.subprocess
```

## Documentation

| Document | Purpose |
| --- | --- |
| [Charter](docs/charter.md) | Why this package exists and what "good" means here |
| [Scope](docs/scope.md) | What is in (and out of) the product |
| [Guides](docs/guides/README.md) | How-tos with tested samples |
| [Docs index](docs/README.md) | Authority order for all of the above |

Charter and scope are product authority. This README is an overview; guides are how-tos.

## Usage

```csharp
using jaytwo.Subprocess;

// ...

var message = "hello world";

var result = await new CliCommandBuilder("node", "-e")
    .WithFormattedArgument("console.log('{0}')", message)
    .GetCommand()
    .RunAsync();

Console.WriteLine("Command: {0}", result.Command);
Console.WriteLine("ExitCode: {0}", result.ExitCode);
Console.WriteLine("StandardOutput: {0}", result.StandardOutput);
Console.WriteLine("StandardError: {0}", result.StandardError);
```

For stdin, encodings, timeouts, cancellation, secrets, platform-specific launch lines, and long-lived workers, start from the [guides](docs/guides/README.md).

**Linux containers:** When using process-tree termination, configure an init/reaper for orphaned children: Docker `--init`, Compose `init: true`, or an init entrypoint in the application image for Kubernetes. See [Docker and Kubernetes deployment guidance](docs/guides/run-across-platforms.md#linux-containers-docker-and-kubernetes).

## Upgrading from 0.2.x

Configure with `CliCommandBuilder`, materialize with `GetCommand()` or `CliCommand.Build(configure)`, then use `command.Run()` / `RunAsync()` for a completed result or `command.Start()` for a live `Streaming.CliProcessSession`. Static `CliCommand.Run(configure)`, `RunAsync(configure)`, and `Start(configure)` combine configuration and launch.

Breaking API changes:

* `ICliCommandExecutor`, `ICancellableCliCommandExecutor`, `ICliCommandExecutorExtensions`, and public `CliCommandExecutor` are removed, including executor-accepting overloads.
* `Execute` / `ExecuteAsync` are replaced by command-level `Run` / `RunAsync`. Builders configure only; materialize a command before running it.
* Public `CliProcessOptions` and `GetProcessOptions()` are removed. Inspect `CliCommand` instead. Start sessions through `CliCommand.Start`, replacing public `CliProcessSession.Start` overloads.
* Defaults apply at the verb. `Start` rejects configured timeout, stdin text/stream, expected exit codes, and output drain timeout; `Run` rejects configured `TerminateTimeout`. Clearing settings restores eligibility. Null or empty expected exit codes are unset; empty stdin text is configured input.
* Configure optional logging with `WithLogger(ILogger?)`. One-shot runs log masked, truncated command/result diagnostics; sessions log launch and termination diagnostics when enabled. Failures while logging one-shot results do not replace command outcomes or change exception suppression. Termination/disposal cleanup survives logger failures.

Other additions include stdin text/stream input, encodings, bounded output drain, cancellation overloads (including suppression), typed cancellation/termination failures, `When`, `WithFormattedArgument`, Streaming sessions, and the net10.0 target. Use `WithFormattedArgument` for formatting into one argv token.

Behavior changes (why this is 0.3.0):

* Captured text is now verbatim. Previously every line was re-joined with `Environment.NewLine` and an extra trailing newline was appended, so
  `"a\n"` became `"a\r\n\r\n"` on Windows. Code that compared raw output (rather than trimming) may see different text.
* Timeout now terminates the whole process tree, and waits for it, instead of killing only the direct process.
* A normal exit or a timeout no longer waits indefinitely for pipe EOF; it gives up after `OutputDrainTimeout` and reports `OutputIncomplete`.
* An empty string in `Secrets` no longer throws from `ToString()`.
* The netstandard2.0 build refuses to run (`PlatformNotSupportedException`) where it cannot terminate a tree (non-Windows without `Process.Kill(bool)`).
* `GetCommand()` snapshots `Secrets`; mutating the builder list afterward does not change an existing command.
* `CliCommandResult.StandardOutput` / `StandardError` are non-null (empty string when there was no text).
* `CliProcessSession.StandardInput.NewLine` defaults to LF (`\n`) instead of `Environment.NewLine`.
* `WithRuntimeCondition` and the `jaytwo.RuntimeRevelation` dependency are gone. Use `When(condition, action)` with a BCL platform
  check (or any other bool) so fluent chains stay concise without a wrapped runtime-info type.
* The old `TimeoutException` type (which embedded output in its message) is removed; timeouts throw `ProcessTimeoutException`.
* `WithArgument(string format, params object[] args)` is removed; use `WithFormattedArgument` or discrete `WithArgument` calls.

---

Made with &hearts; by Jake
