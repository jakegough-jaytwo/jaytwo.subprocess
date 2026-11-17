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

var command = new CliCommandBuilder("node", "-e")
    .WithArgument("console.log('{0}')", message)
    .GetCommand();

var result = await command.ExecuteAsync();

Console.WriteLine("Command: {0}", result.Command);
Console.WriteLine("ExitCode: {0}", result.ExitCode);
Console.WriteLine("StandardOutput: {0}", result.StandardOutput);
Console.WriteLine("StandardError: {0}", result.StandardError);
```

For stdin, encodings, timeouts, cancellation, secrets, platform-specific launch lines, and long-lived workers, start from the [guides](docs/guides/README.md).

## Upgrading from 0.2.x

Additive: new `CliCommand`/`CliCommandBuilder` members (`StandardInput*`, encodings, `OutputDrainTimeout`), `ProcessCanceledException`,
`ProcessTerminationException`, `ICancellableCliCommandExecutor` (a new interface, so existing `ICliCommandExecutor` implementers still compile),
the `CancellationToken` overloads, the `jaytwo.Subprocess.Streaming` namespace and the net10.0 target. The old `TimeoutException`
(which embedded output in its message) is `[Obsolete]`; nothing throws it.

Behavior changes (why this is 0.3.0):

* Captured text is now verbatim. Previously every line was re-joined with `Environment.NewLine` and an extra trailing newline was appended, so
  `"a\n"` became `"a\r\n\r\n"` on Windows. Code that compared raw output (rather than trimming) may see different text.
* Timeout now terminates the whole process tree, and waits for it, instead of killing only the direct process.
* A normal exit or a timeout no longer waits indefinitely for pipe EOF; it gives up after `OutputDrainTimeout` and reports `OutputIncomplete`.
* An empty string in `Secrets` no longer throws from `ToString()`.
* The netstandard2.0 build refuses to run (`PlatformNotSupportedException`) where it cannot terminate a tree (non-Windows without `Process.Kill(bool)`).

---

Made with &hearts; by Jake
