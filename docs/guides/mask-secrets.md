# Mask secrets

Values registered with `WithSecret` are replaced by `****` in `ToString()`, in the logged command line and in logged stdout/stderr. The real output remains on `CliCommandResult.StandardOutput` / `StandardError`.

<!-- sample: mask-secrets -->
```csharp
string apiKey = configuration["ApiKey"];

var command = new CliCommandBuilder("node", "-e")
    .WithArgument("console.log(process.argv[1])")
    .WithArgument("Authorization: Bearer {0}", apiKey)
    .WithSecret(apiKey)
    .GetCommand();

Console.WriteLine(command);   // Authorization header shows **** instead of the key

CliCommandResult result = await command.ExecuteAsync();
// result.StandardOutput is unmasked; do not log it raw if it can echo the secret
```

## Things to know

- Exception messages from this library do not include command arguments or output. Read those from `ex.Result` when you need them, and apply your own redaction before logging.
- An empty string in `Secrets` is ignored for masking and does not throw.
- `CliProcessOptions.Secrets` masks the same way for [long-lived workers](talk-to-a-long-lived-worker.md).
