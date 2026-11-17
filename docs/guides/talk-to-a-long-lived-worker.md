# Talk to a long-lived worker

Most callers should use `CliCommand` for "run a tool and get its output". Use `jaytwo.Subprocess.Streaming.CliProcessSession` when you keep a process alive and speak a request/response protocol over stdin/stdout (for example a Python or JVM worker).

It starts the process with stdin/stdout open and gives you the streams, a bounded stderr drain, and tree termination. It does no framing, pooling, restarts or per-request timeouts.

<!-- sample: session-basic -->
```csharp
using var session = CliProcessSession.Start(
    new CliCommandBuilder("node", workerScript, outputDirectory)
        .GetProcessOptions());
session.StandardInput.NewLine = "\n";

await session.StandardInput.WriteLineAsync(request);
string? response = await session.StandardOutput.ReadLineAsync();   // null once the worker is gone
```

Namespaces: `jaytwo.Subprocess`, `jaytwo.Subprocess.Streaming`.


## Per-request timeout

A pending `ReadLineAsync` cannot itself be canceled on every framework. Terminate the worker to unblock it:

<!-- sample: session-timeout -->
```csharp
using var timeout = new CancellationTokenSource(requestTimeout);
using var registration = timeout.Token.Register(session.Terminate);

string? response = await session.StandardOutput.ReadLineAsync();
```

`requestTimeout` is your per-request deadline (for example `TimeSpan.FromSeconds(30)`).

## Lifecycle

<!-- sample: session-lifecycle -->
```csharp
int pid = session.ProcessId;           // telemetry
Process process = session.Process;     // borrowed; do not dispose or use its redirected streams

session.Terminate();                   // kill the tree; idempotent; check HasExited if needed
int exitCode = await session.WaitForExitAsync(token);   // cancel abandons the wait without killing
```

`Dispose()` terminates the whole tree if it is still running and releases pipes and the stderr drain. Do not use `StandardInput` / `StandardOutput` after dispose.

## Options

`CliProcessOptions` has only launch and terminate settings: file name, arguments, working directory, environment, secrets, encodings and `TerminateTimeout`. There is no one-shot `Timeout`, `ExpectedExitCodes` or stdin blob; those belong on `CliCommand`. Prefer `CliCommandBuilder.GetProcessOptions()` so quoting and launch settings match one-shot runs. `new CliProcessOptions(fileName, arguments)` still accepts an already-quoted argument string when you build options by hand.

<!-- sample: session-options -->
```csharp
var options = new CliCommandBuilder("node", workerScript, outputDirectory)
    .WithWorkingDirectory(workerDirectory)
    .WithEnvironmentVariable("NODE_ENV", "test")
    .GetProcessOptions();
options.TerminateTimeout = TimeSpan.FromSeconds(10);

using var session = CliProcessSession.Start(options);
```

## Things to know

- `StandardError` is the last 64K characters of stderr, drained continuously so a chatty worker cannot block on a full pipe.
- Restarting a dead worker, correlating requests and per-request deadlines belong to the caller.
- Prefer `GetProcessOptions()` when starting a session from a builder. See [Pass arguments](pass-arguments.md).
