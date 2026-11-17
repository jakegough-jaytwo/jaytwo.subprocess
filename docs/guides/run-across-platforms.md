# Run across platforms

Some CLIs ship under different names (or as `.exe` / `.cmd` shims) depending on the OS. Keep the branch on the fluent builder with `When`, using BCL platform checks for the condition.

`where` (Windows) and `which` (Unix) are the same idea: find an executable on `PATH`. The sample looks up `node` so it runs in ordinary CI:

<!-- sample: platform-branch -->
```csharp
CliCommandResult result = await new CliCommandBuilder()
    .When(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), b => b.WithFileName("where").WithArgument("node"))
    .When(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows), b => b.WithFileName("which").WithArgument("node"))
    .GetCommand()
    .RunAsync();
```

Namespaces: `jaytwo.Subprocess`, `System.Runtime.InteropServices` (`RuntimeInformation`, `OSPlatform`). Prefer `RuntimeInformation.IsOSPlatform` when you still target netstandard2.0 / .NET Framework; on modern TFMs `OperatingSystem.IsWindows()` is fine too.

The same pattern covers npm-style Windows shims (`yo.cmd` / `cmd /c yo` vs a Unix `yo` on `PATH`): only the file name (and sometimes wrapping) changes.

## Shared setup after the branch

<!-- sample: platform-shared -->
```csharp
var command = new CliCommandBuilder()
    .When(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), b => b.WithFileName("where").WithArgument(toolName))
    .When(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows), b => b.WithFileName("which").WithArgument(toolName))
    .WithWorkingDirectory(projectDirectory)
    .WithTimeout(TimeSpan.FromMinutes(2))
    .GetCommand();
```

Timeout, working directory, environment and secrets applied after the branch apply on every platform.

## Things to know

- `When` only runs the action when the condition is true; it does not carry platform info of its own.
- Prefer launching the real binary when you can resolve it; wrapping with `cmd /c` or a shell is a last resort for tools that only exist as shell scripts.
- Cross-platform differences in path separators and shell builtins still belong to your arguments; the builder only quotes them.
