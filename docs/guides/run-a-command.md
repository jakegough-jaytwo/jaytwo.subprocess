# Run a command

Build a `CliCommand`, run it, and read the decoded stdout/stderr plus the outcome.

<!-- sample: run-basic -->
```csharp
var command = new CliCommandBuilder("node", "-e")
    .WithArgument("console.log('{0}')", "hello world")
    .GetCommand();

CliCommandResult result = await command.ExecuteAsync();

Console.WriteLine(result.Command);           // masked ToString of the launch line
Console.WriteLine(result.ExitCode);
Console.WriteLine(result.StandardOutput);
Console.WriteLine(result.StandardError);
Console.WriteLine(result.Duration);
Console.WriteLine(result.Success);           // true when the exit code was expected
```

Namespace: `jaytwo.Subprocess`.


`Execute` and `ExecuteAsync` are extension methods on `CliCommand`. They construct a `CliCommandExecutor` unless you pass an `ICliCommandExecutor` (or `ICancellableCliCommandExecutor` when you also pass a `CancellationToken`).

## Sync or async

<!-- sample: run-sync-async -->
```csharp
CliCommandResult syncResult = command.Execute();
CliCommandResult asyncResult = await command.ExecuteAsync();
```

Both drain stdout and stderr concurrently, wait for the process to exit, and return the same `CliCommandResult` shape.

## Inject an executor

<!-- sample: run-inject-executor -->
```csharp
ICliCommandExecutor executor = new CliCommandExecutor();
CliCommandResult result = await command.ExecuteAsync(executor);

// or with a token (requires ICancellableCliCommandExecutor)
CliCommandResult canceledAware = await command.ExecuteAsync(token, executor);
```

Use this for tests or when your app already owns a shared executor. A custom `ICliCommandExecutor` that does not implement `ICancellableCliCommandExecutor` rejects a cancelable token with `NotSupportedException` rather than ignoring it.

## Things to know

- Default expected exit code is `0`. Anything else throws `UnexpectedExitCodeException` unless you change `ExpectedExitCodes` or pass `suppressCommandLineExceptions: true`. See [Timeouts, cancellation and exit codes](timeouts-cancellation-and-exit-codes.md).
- A missing program throws the `Win32Exception` from `Process.Start` before cleanup is needed.
- `result.ToString()` summarizes outcome, duration and the masked command line; it is handy for logs.

See also: [Pass arguments](pass-arguments.md), [Capture output](capture-output.md).
