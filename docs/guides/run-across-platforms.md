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

## Linux containers: Docker and Kubernetes

When an application uses subprocesses in a Linux container, ensure its process namespace has an init process that reaps orphaned children. This applies to production applications as well as CI, including timeout/cancellation of one-shot commands and `Terminate` / `Dispose` on worker sessions.

### Termination and reaping are different

Tree termination stops processes. Reaping collects an exited child's status and removes its remaining process-table entry. If a parent exits before collecting its child's status, the child can be adopted by PID 1 (or a subreaper). That process must reap it; see the Linux [wait(2) documentation](https://man7.org/linux/man-pages/man2/wait.2.html).

Without a working reaper, a killed descendant can remain as a zombie (`Z` in `ps`, often shown as `<defunct>`). It is no longer executing and does not hold pipes open, but PID-based liveness checks can still find it. Over time, zombies consume process-table entries. A surviving PID alone therefore does not prove tree termination failed.

### Docker and Compose

For an image that does not already provide an init/reaper, enable Docker's init process when starting the application container:

```sh
docker run --init your-image:tag
```

Docker's [`--init` option](https://docs.docker.com/engine/containers/multi-service_container/) inserts a small init process ahead of the application's entrypoint. Use it for the container that actually runs the application or tests, including CI containers started with Jenkins `docker.image(...).inside('--init')`.

With Docker Compose, configure [`init: true`](https://docs.docker.com/reference/compose-file/services/#init) on that service:

```yaml
services:
  app:
    image: your-image:tag
    init: true
```

### Kubernetes

For Kubernetes, include an init such as [Tini](https://github.com/krallin/tini) in the application's final runtime image and launch the application through it. An image entrypoint also works when running the same image outside Kubernetes.

For example, in a Debian/Ubuntu-based runtime stage, before switching to an unprivileged application user:

```dockerfile
RUN apt-get update \
    && apt-get install -y --no-install-recommends tini \
    && rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["/usr/bin/tini", "--", "dotnet", "YourApp.dll"]
```

Replace `YourApp.dll` with the application's assembly and set its working directory as usual. Tini's installation method and path depend on the base image; its documentation also covers Alpine and other images. If the container shares a process namespace and Tini is not PID 1, configure it as a subreaper with `tini -s -- ...` as described in [Tini's subreaping guidance](https://github.com/krallin/tini#subreaping).

A Pod's [`command` field overrides the image entrypoint](https://kubernetes.io/docs/tasks/inject-data-application/define-command-argument-container/). Leave it unset to retain this entrypoint, or include Tini in the override. A Kubernetes [init container](https://kubernetes.io/docs/concepts/workloads/pods/init-containers/) is a separate startup container; it does not wrap or reap the application container's children.

### Diagnose a suspected surviving descendant

Inside the affected container, inspect the process state and parent:

```sh
ps -eo pid,ppid,stat,comm
```

Use an image with `ps` available, or equivalent container diagnostics. A `Z` state indicates an exited process awaiting reaping; a live descendant needs a different investigation. Check the actual image entrypoint and runtime configuration, since development, CI and production can launch the same image differently.

In our Linux CI reproduction, a container running `cat` as PID 1 left a killed `sleep` descendant in state `Z`; adding `--init` let the same termination test pass.

An init process handles reaping; it does not extend the library's tree-termination reach to live descendants orphaned before termination or spawned during it. Keep `OutputDrainTimeout` and inspect `OutputIncomplete` for live descendants holding pipes open; increasing the drain timeout does not reap zombies. See [Capture output](capture-output.md) and [Timeouts, cancellation and exit codes](timeouts-cancellation-and-exit-codes.md).

## Things to know

- `When` only runs the action when the condition is true; it does not carry platform info of its own.
- Prefer launching the real binary when you can resolve it; wrapping with `cmd /c` or a shell is a last resort for tools that only exist as shell scripts.
- Cross-platform differences in path separators and shell builtins still belong to your arguments; the builder only quotes them.
