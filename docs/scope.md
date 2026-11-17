# Scope

This document lists what belongs in `jaytwo.Subprocess` and what does not.

The [charter](charter.md) defines purpose and principles. This document is the capability boundary. [Guides](guides/README.md) show how to use what is in scope; they are not product authority.

The italicized sentence under each item sketches the mechanism without defining every edge case.

## One-shot execution

### Run a command

Start a process from a file name and arguments, wait for it to finish, and return a result with exit code, duration, and captured output.

*Builds a `ProcessStartInfo`, starts the process, drains output, and waits for exit (or timeout/cancel).*

### Fluent command building

Compose file name, arguments, working directory, environment, timeout, expected exit codes, stdin, encodings, and secrets through `CliCommandBuilder`. Session-only `TerminateTimeout` and optional logging are also set on the same builder. `CliCommand.Run` / `RunAsync` finish a one-shot run; `CliCommand.Start` returns a caller-owned session.

*Accumulates settings and produces a `CliCommand` with safely quoted arguments. Build preserves unset values; each verb applies its defaults and rejects settings for the other lifetime.*

### Expected exit codes

Treat a configured set of exit codes as success (default `0`). Other codes fail with a typed exception that still carries the result.

*Compares `process.ExitCode` to the configured list after a normal exit.*

## Output capture

### Concurrent drain

Read stdout and stderr from process start so large simultaneous output cannot deadlock.

*Background readers append decoded chunks into buffers until EOF or the drain bound.*

### Verbatim text

Preserve decoded text exactly, including blank lines, original newline sequences, and a missing final newline. Do not rebuild output with `AppendLine`.

*Appends reader chunks to a string builder without inserting extra newlines.*

### Encodings

Allow separate encodings for stdout and stderr (and for text stdin). Null keeps the platform default of `System.Diagnostics.Process`.

*Sets `ProcessStartInfo` standard stream encodings before start.*

### Bounded drain

After the process ends (or is terminated), wait only up to `OutputDrainTimeout` for streams to finish. Report `OutputIncomplete` when EOF never arrives (typically a descendant still holding redirected pipes).

*Waits on the reader tasks with a timeout; keeps whatever text was already read.*

## Standard input (one-shot)

### Text or stream input

Supply stdin as text or as a caller-owned stream without building `ProcessStartInfo` by hand. Feed it concurrently with output draining; close stdin when input is exhausted so EOF-dependent tools can exit.

*Redirects stdin, copies bytes on a background task, then closes the writer.*

## Timeout, cancellation, and termination

### Timeout

Enforce a configurable timeout (default 60 seconds; infinite allowed). On timeout, terminate the process tree, wait for exit, drain output within the bound, and report `TimedOut`.

*Arms a timer; on fire, kills the tree and completes with a timeout result/exception.*

### Cancellation

Honor a `CancellationToken` on `Run` / `RunAsync`. Distinguish caller cancel from timeout and from unexpected exit. Cancellation is never swallowed by `suppressCommandLineExceptions`.

*Registers the token; on cancel, kills the tree and throws `ProcessCanceledException`.*

### Process-tree termination

On timeout or cancel, terminate the whole process tree. Never silently fall back to killing only the direct process. Where tree kill is impossible (for example netstandard2.0 on Mono/Linux), refuse to start.

*Uses `Process.Kill(entireProcessTree: true)`, reflection to that API, or `taskkill /T` on .NET Framework.*

### Failed termination

If termination was attempted and the process has not exited within the drain/terminate wait, fail with a typed result (`TerminationFailed`) rather than hanging or throwing a bare exception with no result.

*Bounds the wait after kill; builds a partial result from text already read.*

## Diagnostics

### Secrets masking

Mask configured secrets in `ToString`, logged command lines, and logged output. Result properties keep the real text. Exception messages do not embed command arguments or output.

*Replaces known secret substrings with a fixed mask in diagnostic strings.*

### Optional logging

Accept an optional non-generic `ILogger` through `WithLogger`; do not add a logging dependency solely for telemetry on modern TFMs beyond the existing package references.

*Writes truncated, masked command and result lines for one-shot runs, and launch/termination diagnostics for sessions, when the relevant logging level is enabled. Logger failures cannot interrupt termination or disposal.*

### Process id on the result

Expose the child PID on `CliCommandResult` after a one-shot run (including failure results) for post-hoc telemetry.

*Records `process.Id` after start onto the result.*

## Cross-platform launch helpers

### Conditional builder configuration

Apply builder actions only when a caller-supplied condition is true (for example a BCL OS check), so platform-specific file names stay on the fluent chain.

*Evaluates a bool and invokes the action on the same builder when true (`When`).*

## Streaming (escape hatch)

### Process session

Start a long-lived process with stdin/stdout open for the caller, drain stderr in the background (bounded tail), expose PID/`Process` for telemetry, and terminate the tree on `Terminate` or `Dispose`.

*Uses the same launch and tree-kill machinery as one-shot; does not wait for a final "result" object.*

`CliCommand.Start` accepts launch and terminate settings. It rejects currently configured one-shot timeout, expected exit codes, stdin text/stream, and output drain timeout. `Run` / `RunAsync` reject a configured `TerminateTimeout`. Null settings and null or empty expected exit codes are unset; empty stdin text remains configured input. Clearing a setting restores eligibility for the other verb.

## Boundaries

The scope excludes:

- Competing with CliWrap (or similar) on breadth of adapters, pipelines, and fluent execution styles
- Process pools, worker supervisors, request/response framing, or protocol helpers
- Per-request deadlines inside the session API (callers compose `CancellationToken` + `Terminate`)
- Cancelling a pending `ReadLineAsync` without terminating the process (framework limitation; `Terminate` is the unblock)
- Silent parent-only kill when tree termination is unavailable
- Native job objects or POSIX process groups, unless a concrete hang remains after the drain bound and the owner approves the dependency/interop cost
- Mid-flight one-shot start callbacks that hand out a live `Process` (use the session escape hatch when you need live process access during a long run)
- Guaranteeing kill of descendants orphaned before termination or spawned during it (the drain bound protects the caller; orphans may survive)

Opening or starting a process does not promise every descendant can be reaped. Unsupported platforms and prohibited combinations fail explicitly.
