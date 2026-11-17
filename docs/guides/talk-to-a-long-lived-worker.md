# Talk to a long-lived worker

Most callers should use `CliCommand` for "run a tool and get its output". Use `jaytwo.Subprocess.Streaming.CliProcessSession` when you keep a process alive and speak a request/response protocol over stdin/stdout (for example a Python or JVM worker).

It starts the process with stdin/stdout open and gives you the streams, a bounded stderr drain, and tree termination. It does no framing, pooling, restarts or per-request timeouts.

<!-- sample: session-basic -->
```csharp
using var session = new CliCommandBuilder("node", workerScript, outputDirectory).GetCommand().Start();

await session.StandardInput.WriteLineAsync(request);
string? response = await session.StandardOutput.ReadLineAsync();   // null once the worker is gone
```

Namespaces: `jaytwo.Subprocess`, `jaytwo.Subprocess.Streaming`.

`StandardInput.NewLine` defaults to LF (`\n`) so line protocols work the same on Windows and Unix. Set it after start only if you need CRLF.


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

For workers in Linux containers, also configure an init/reaper so terminated descendants can be reaped. See [Docker and Kubernetes deployment guidance](run-across-platforms.md#linux-containers-docker-and-kubernetes).

## Options

Configure a `CliCommand`, then call `Start()`, or use `CliCommand.Start(configure)`. `Start` rejects currently configured one-shot `Timeout`, `ExpectedExitCodes`, stdin text/stream, and `OutputDrainTimeout`; clear those settings before starting a session. An ordinary built command leaves them unset. `TerminateTimeout` controls the session's wait after tree termination and is rejected by `Run` / `RunAsync`. For already-quoted launch arguments, set `CliCommand.Arguments` directly. `session.Command` retains the masked launch line captured before start.

<!-- sample: session-options -->
```csharp
using var session = new CliCommandBuilder("node", workerScript, outputDirectory)
        .WithWorkingDirectory(workerDirectory)
        .WithEnvironmentVariable("NODE_ENV", "test")
        .WithTerminateTimeout(TimeSpan.FromSeconds(10)).GetCommand().Start();
```

## Things to know

- `StandardError` is the last 64K characters of stderr, drained continuously so a chatty worker cannot block on a full pipe.
- Restarting a dead worker, correlating requests and per-request deadlines belong to the caller.
- Prefer `builder.GetCommand().Start()` when starting a session from a builder. See [Pass arguments](pass-arguments.md).
