# Pass arguments

`CliCommandBuilder` collects the file name and arguments, then `GetCommand()` produces a launch line whose arguments are already quoted for `ProcessStartInfo`.

<!-- sample: pass-arguments-basic -->
```csharp
CliCommandResult result = await new CliCommandBuilder("node", scriptPath)
    .WithArgument("--message")
    .WithArgument(message)
    .WithWorkingDirectory(workingDirectory)
    .WithEnvironmentVariable("MY_TOOL_PROMPT", "0")
    .GetCommand()
    .RunAsync();
```

Arguments with spaces and special characters are quoted for you. Pass each logical argument separately; do not pre-join them into one string unless you intend a single argument.

## Formatted arguments

<!-- sample: pass-arguments-format -->
```csharp
var command = new CliCommandBuilder("node", "-e")
    .WithFormattedArgument("console.log('{0}')", message)
    .GetCommand();
```

`WithFormattedArgument` is `string.Format` then one argument. Prefer it when the tool expects a single script or expression string. For two argv tokens, call `WithArgument` twice (or pass them to the constructor / `WithArguments`) — there is no `WithArgument(flag, value)` overload.

## Working directory and environment

<!-- sample: pass-arguments-env -->
```csharp
var command = new CliCommandBuilder("node", scriptPath)
    .WithWorkingDirectory(projectDirectory)
    .WithEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en")
    .WithEnvironmentVariables(extraVars)
    .GetCommand();
```

Environment entries are merged into the child's environment for that launch only.

## Start a session from the builder

When you need the same quoting for a long-lived session, materialize the command and call `Start`:

<!-- sample: pass-arguments-session -->
```csharp
using var session = new CliCommandBuilder("node", workerScript, outputDirectory, "--mode", mode).GetCommand().Start();
```


See [Talk to a long-lived worker](talk-to-a-long-lived-worker.md).

## Things to know

- `CliCommandBuilder.ToString()` and `CliCommand.ToString()` show the launch line with secrets masked. See [Mask secrets](mask-secrets.md).
- Initial arguments can be passed to the constructor: `new CliCommandBuilder("tool", "a", "b")`.
- For OS-specific file names or wrappers, use [Run across platforms](run-across-platforms.md).
