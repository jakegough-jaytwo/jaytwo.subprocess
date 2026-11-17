# Run a command

Build a command with `CliCommandBuilder`, run it, and read the decoded stdout/stderr plus the outcome.

<!-- sample: run-basic -->
```csharp
CliCommandResult result = await new CliCommandBuilder("node", "-e")
    .WithFormattedArgument("console.log('{0}')", "hello world")
    .GetCommand()
    .RunAsync();

Console.WriteLine(result.Command);           // masked ToString of the launch line
Console.WriteLine(result.ExitCode);
Console.WriteLine(result.StandardOutput);
Console.WriteLine(result.StandardError);
Console.WriteLine(result.Duration);
Console.WriteLine(result.Success);           // true when the exit code was expected
```

Namespace: `jaytwo.Subprocess`.

`CliCommandBuilder` configures a command. Call `GetCommand()` (or `CliCommand.Build(configure)`) to inspect it, then `Run` / `RunAsync` to finish a one-shot run. `CliCommand.Run(configure)` / `RunAsync(configure)` configure and run directly. Use `CliCommand.Start(configure)` or `command.Start()` for a live session.

## Sync or async

<!-- sample: run-sync-async -->
```csharp
CliCommandResult syncResult = command.Run();
CliCommandResult asyncResult = await command.RunAsync();
```

Both drain stdout and stderr concurrently, wait for the process to exit, and return the same `CliCommandResult` shape.

## Optional logging

Pass an existing `Microsoft.Extensions.Logging.ILogger` to `WithLogger`. Command lines and output are masked and truncated in diagnostics; result properties retain the real text.

<!-- sample: run-logging -->
```csharp
CliCommandResult result = await CliCommand.RunAsync(b => b
    .WithFileName("node")
    .WithArgument("-e")
    .WithArgument("console.log('ok')")
    .WithLogger(logger));
```

`Start` also logs masked, truncated launch and termination diagnostics when a logger is configured and information logging is enabled. It does not log session streams.

One-shot result logging is best effort: logger failures do not replace the result or typed process exception, and do not change `suppressCommandLineExceptions` behavior. Launch-logging failures still propagate. If session logging fails after launch, the started session is cleaned up before the failure is rethrown. Termination and disposal diagnostics do not throw.

## Things to know

- Default expected exit code is `0`. Anything else throws `UnexpectedExitCodeException` unless you change `ExpectedExitCodes` or pass `suppressCommandLineExceptions: true`. See [Timeouts, cancellation and exit codes](timeouts-cancellation-and-exit-codes.md).
- A missing program throws the `Win32Exception` from `Process.Start` before cleanup is needed.
- `result.ToString()` summarizes outcome, duration and the masked command line; it is handy for logs.

See also: [Pass arguments](pass-arguments.md), [Capture output](capture-output.md).
