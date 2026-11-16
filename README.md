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

A simplified way to invoke external processes in .NET

## Installation

Add the NuGet package

```
PM> Install-Package jaytwo.subprocess
```

## Documentation

- [Guides](docs/guides/README.md) — How to run tools, capture output, feed stdin, handle timeouts and talk to long-lived workers.

## Usage

Run a CLI, get back its decoded stdout/stderr and a clear outcome (exit code, timeout, or cancel). It's got a fluent command builder that
makes things pretty easy, and its usage should be pretty easy to figure out. The hard parts (draining both streams, killing the whole process
tree on timeout, argument quoting, secrets masking) are handled for you.

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

## Running tools reliably

### Output capture

`StandardOutput` and `StandardError` are the decoded text exactly as the child wrote it: blank lines, `\r\n` vs `\n` vs `\r`, and a
missing final newline are all preserved. Both streams are drained concurrently from process start, so large simultaneous output on both
cannot deadlock. No ordering between stdout and stderr is promised.

Encodings default to the platform default of `System.Diagnostics.Process`. Set them explicitly (they are independent):

```csharp
var command = new CliCommandBuilder("tool")
    .WithStandardOutputEncoding(new UTF8Encoding(false))
    .WithStandardErrorEncoding(Encoding.GetEncoding("iso-8859-1"))
    .GetCommand();
```

If a stream does not reach end-of-file within `OutputDrainTimeout` (default 5 seconds) after the process ends - usually because a
descendant that outlived it still holds the redirected pipes - the call stops waiting and sets `result.OutputIncomplete`. The text read so far
is kept. This is the bound that prevents an unbounded wait.

### Standard input

Without any input configured, stdin is not redirected (the child inherits the host's stdin, as before). Supply one of:

```csharp
// text: UTF-8 without a BOM unless you pass an encoding
var command = new CliCommandBuilder("openssl", "base64").WithStandardInput("hello").GetCommand();

// or bytes; copied from the stream's current position to its end
var command = new CliCommandBuilder("qpdf", "-", "out.pdf").WithStandardInput(pdfStream).GetCommand();
```

* stdin is fed concurrently with output draining and **always closed when the input is exhausted**, so tools that wait for EOF can exit.
  `WithStandardInput("")` gives the child an immediate EOF.
* The caller owns an input stream: it is never disposed or rewound, and should not be touched until the call returns.
* If the child exits or closes stdin early (a broken pipe), that is not an error; the remaining input is dropped and the exit code decides success.
* If reading the caller's stream throws, the process tree is terminated and that exception is rethrown.
* On timeout or cancellation the process tree is terminated, which also ends a blocked write.
* Text and stream input are mutually exclusive (`ArgumentException`).

### Timeout, cancellation and exit codes

```csharp
using var cts = new CancellationTokenSource();
var result = await command.ExecuteAsync(cts.Token);   // or command.Execute(cts.Token)
```

| Outcome | Thrown | `Result` flags |
|---|---|---|
| exit code not in `ExpectedExitCodes` (default `0`) | `UnexpectedExitCodeException` | `Success == false` |
| `Timeout` elapsed (default 60 seconds, `Timeout.InfiniteTimeSpan` allowed) | `ProcessTimeoutException` | `TimedOut` |
| the token was canceled | `ProcessCanceledException` (an `OperationCanceledException`) | `Canceled` |
| the token was already canceled before the call | `OperationCanceledException`, nothing started | - |
| terminate was attempted but the process did not exit within `OutputDrainTimeout` | `ProcessTerminationException` | `TerminationFailed` (plus `TimedOut`/`Canceled`); partial output, `ExitCode` not meaningful |

The timeout, exit-code and termination exceptions derive from `SubprocessException`; `suppressCommandLineExceptions: true` returns their `Result`
instead of throwing, but never swallows a cancellation (`ProcessCanceledException`, or any result with `Canceled` set). Every failure result carries both captured streams.

On timeout or cancellation the library terminates the **process tree**, waits for the process to exit, then drains output (bounded by
`OutputDrainTimeout`). If the process exits on its own at the same moment a timeout or cancellation fires, the natural exit wins and is
reported normally; you never get both, and nothing hangs. A launch failure (for example the program does not exist) throws the
`Win32Exception` from `Process.Start` before anything has to be cleaned up. Process objects, registrations and readers are released on every path.

Tree termination, per framework (it never falls back to killing only the parent):

| Target | Mechanism |
|---|---|
| net6.0, net8.0, net10.0 | `Process.Kill(entireProcessTree: true)` |
| netstandard2.0 on .NET Core 3.0+ / .NET 5+ | the same method, found by reflection |
| netstandard2.0 on .NET Framework (Windows) | `taskkill /PID <pid> /T /F` |
| netstandard2.0 on anything else (for example Mono on Linux) | `PlatformNotSupportedException` before the process starts |

Limitation: termination finds descendants through their parent process ids. A descendant that was orphaned before the kill (its parent
exited first) or that is spawned during the kill can survive. That is exactly what `OutputDrainTimeout` protects the caller against.

### Diagnostics and secrets

`CliCommand.Secrets` are masked as `****` in `ToString()`, in the logged command and in the logged stdout/stderr. `CliCommandResult.StandardOutput`
itself is the real output. The messages of the exceptions this library throws do not include command arguments or output.

### Upgrading from 0.2.x

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

## Advanced Usage

There are a few features that you'll probably never use or need... but I needed them, so maybe you will too.

### Runtime Conditions

Sometimes invoking commands just does't work the same on different platforms.  For example, if my app is developed on Windows but deployed
to a Linux container, I would like it to work reliably in both places.

The [Yeoman NPM Package](https://www.npmjs.com/package/yo) can be invoked on linux with with `yo` directly, but no such luck on Windows.  
(Even though it works from a Bash or CMD shell on Windows... if you run `which yo` in Bash or `where yo` in CMD, and you'll see that 
`yo` just points to shortcuts/scripts that the shell knows what  to do with.)  To work around this, we can invoke `yo` from the command 
interpreter itself on windows with `cmd /c yo`.

```csharp
using jaytwo.Subprocess;

// ...

var message = "hello world";

var command = new CliCommandBuilder()
    .WithRuntimeCondition(x => x.Platform != OSPlatform.Windows, builder =>
    {
        // specific for non-Windows platforms
        builder.WithFileName("yo");
    })
    .WithRuntimeCondition(x => x.Platform == OSPlatform.Windows, builder =>
    {
        // specific for Windows platform
        builder.WithFileName("cmd").WithArguments(new[] { "/C", "yo" });
    })
    .GetCommand();
```

### Streaming (advanced, opt-in)

Most callers never need this. `jaytwo.Subprocess.Streaming.CliProcessSession` is a small primitive for a process you keep alive and talk
to live (for example a Python or JVM worker): it starts the process with stdin/stdout open and gives you the streams, a bounded stderr drain,
and tree termination. It does no framing, pooling, restarts or per-request timeouts. It takes `CliProcessOptions`, which has only launch and
terminate settings (file name, arguments, working directory, environment, secrets, encodings, `TerminateTimeout`); there is no `Timeout`,
`ExpectedExitCodes` or one-shot stdin on it.

```csharp
using jaytwo.Subprocess.Streaming;

var arguments = new CliCommandBuilder("python", "worker.py").GetCommand().Arguments;   // safely quoted
using var session = CliProcessSession.Start(new CliProcessOptions("python", arguments));
session.StandardInput.NewLine = "\n";

await session.StandardInput.WriteLineAsync(request);
var response = await session.StandardOutput.ReadLineAsync();   // null once the worker is gone

// per-request timeout: terminate the worker (the pending read then returns null)
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var registration = timeout.Token.Register(session.Terminate);

var pid = session.ProcessId;   // telemetry; session.Process is the live (borrowed) Process
```

* The session owns the process, the pipes and the stderr drain. `Dispose()` terminates the whole tree if it is still running. `StandardInput` and
  `StandardOutput` are borrowed from the session.
* `Terminate()` kills the tree and waits (bounded by `TerminateTimeout`) for exit. It is idempotent, safe from any thread, and does not throw if the
  process will not die; check `HasExited`.
* `WaitForExitAsync(token)` returns the exit code; canceling the token abandons the wait without killing anything.
* `StandardError` is the last 64K characters of stderr, drained continuously so a chatty worker cannot block on a full pipe.
* A pending `ReadLineAsync` cannot itself be canceled on every framework; `Terminate()` is how you unblock it. Restarting a dead worker,
  correlating requests and per-request deadlines belong to the caller.

---

Made with &hearts; by Jake
