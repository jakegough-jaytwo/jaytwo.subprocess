# Capture output

`StandardOutput` and `StandardError` are the decoded text exactly as the child wrote it: blank lines, `\r\n` vs `\n` vs `\r`, and a missing final newline are all preserved. Both streams are drained concurrently from process start so large simultaneous output cannot deadlock. No ordering between stdout and stderr is promised.

<!-- sample: capture-basic -->
```csharp
CliCommandResult result = await new CliCommandBuilder("node", "-e")
    .WithArgument("console.log('line1'); console.error('err1')")
    .GetCommand()
    .RunAsync();

string stdout = result.StandardOutput;
string stderr = result.StandardError;
```

## Choose encodings

Encodings default to the platform default of `System.Diagnostics.Process`. Set them independently when you know the tool's encoding:

<!-- sample: capture-encodings -->
```csharp
var command = new CliCommandBuilder("node", encodingScript, "utf8", "latin1")
    .WithStandardOutputEncoding(new UTF8Encoding(false))
    .WithStandardErrorEncoding(Encoding.GetEncoding("iso-8859-1"))
    .GetCommand();
```

## Incomplete drain

If a stream does not reach end-of-file within `OutputDrainTimeout` (default 5 seconds) after the process ends - usually because a descendant that outlived it still holds the redirected pipes - the call stops waiting and sets `result.OutputIncomplete`. The text read so far is kept.

<!-- sample: capture-incomplete -->
```csharp
var command = new CliCommandBuilder("node", scriptPath, workingDirectory)
    .WithOutputDrainTimeout(TimeSpan.FromSeconds(2))
    .GetCommand();

CliCommandResult result = await command.RunAsync(suppressCommandLineExceptions: true);

if (result.OutputIncomplete)
{
    // treat captured text as a prefix; a descendant may still be alive
}

```

This bound prevents an unbounded wait after exit or termination.

## Things to know

- Captured text is verbatim. Do not assume lines were re-joined with `Environment.NewLine` or that a trailing newline was added.
- Secrets are masked in logged stdout/stderr for diagnostics, but `CliCommandResult.StandardOutput` and `StandardError` are the real output. See [Mask secrets](mask-secrets.md).
- Exception messages from this library do not include command arguments or output; read them from `ex.Result` instead.
