# Feed standard input

Without input configured, stdin is not redirected and the child inherits the host's stdin. To send data, use text or a stream; stdin is always closed when that input is exhausted so tools that wait for EOF can exit.

## Text

<!-- sample: stdin-text -->
```csharp
var command = new CliCommandBuilder("node", echoScript)
    .WithStandardInput("hello")
    .GetCommand();

CliCommandResult result = await command.RunAsync();
Console.WriteLine(result.StandardOutput);
```

Text is encoded as UTF-8 without a BOM unless you pass an encoding to `WithStandardInput(text, encoding)`. `WithStandardInput("")` gives the child an immediate EOF.

## Bytes from a stream

<!-- sample: stdin-stream -->
```csharp
using FileStream inputStream = File.OpenRead(inputPath);

var command = new CliCommandBuilder("node", hexScript)
    .WithStandardInput(inputStream)
    .GetCommand();

CliCommandResult result = await command.RunAsync();
```

Bytes are copied from the stream's current position to its end. The caller owns the stream: it is never disposed or rewound, and must not be used until the call returns.

## Things to know

- Text and stream input are mutually exclusive (`ArgumentException`).
- Input is written concurrently with output draining. If the child exits or closes stdin early (broken pipe), the remaining input is dropped and the exit code decides success.
- If reading the caller's stream throws, the process tree is terminated and that exception is rethrown.
- On timeout or cancellation the process tree is terminated, which also ends a blocked write. See [Timeouts, cancellation and exit codes](timeouts-cancellation-and-exit-codes.md).
