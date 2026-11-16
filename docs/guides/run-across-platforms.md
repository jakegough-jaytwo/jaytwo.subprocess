# Run across platforms

When the same tool is launched differently on Windows and elsewhere, describe both paths with `WithRuntimeCondition`. Only the matching branch runs against the current `RuntimeInformation`.

A common case is an npm global CLI such as `yo`: a real executable on Unix, but a shell wrapper on Windows that needs `cmd /c yo`. The samples below branch on platform the same way (using `node` so they run everywhere):

<!-- sample: platform-branch -->
```csharp
var command = new CliCommandBuilder()
    .WithRuntimeCondition(x => x.Platform != OSPlatform.Windows, builder =>
    {
        builder.WithFileName("node").WithArguments(new[] { "-e", "console.log('unix')" });
    })
    .WithRuntimeCondition(x => x.Platform == OSPlatform.Windows, builder =>
    {
        builder.WithFileName("node").WithArguments(new[] { "-e", "console.log('windows')" });
    })
    .GetCommand();

await command.ExecuteAsync();
```

Namespaces: `jaytwo.Subprocess`, `jaytwo.RuntimeRevelation` (`OSPlatform`).

## Shared setup after the branch

<!-- sample: platform-shared -->
```csharp
var command = new CliCommandBuilder()
    .WithRuntimeCondition(x => x.Platform == OSPlatform.Windows, b => b.WithFileName("node").WithArguments(new[] { "-e", "console.log(process.argv[1])", name }))
    .WithRuntimeCondition(x => x.Platform != OSPlatform.Windows, b => b.WithFileName("node").WithArguments(new[] { "-e", "console.log(process.argv[1])", name }))
    .WithWorkingDirectory(projectDirectory)
    .WithTimeout(TimeSpan.FromMinutes(2))
    .GetCommand();
```

Arguments, timeout, working directory and secrets applied outside a condition apply on every platform.

## Things to know

- Conditions are evaluated when you call the builder method, using `RuntimeInformation.Current` (or the runtime info injected into the builder for tests).
- Prefer launching the real binary when you can resolve it; wrapping with `cmd /c` or a shell is a last resort for tools that only exist as shell scripts.
- Cross-platform differences in path separators and shell builtins still belong to your arguments; the builder only quotes them.
