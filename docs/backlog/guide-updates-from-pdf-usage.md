# Task: guide updates from real-world session usage

Status: Ready. The owner asked for this task on 2026-11-17. It changes guides and guide tests only. It does not change library code, the charter, the scope, or AGENTS.md.

Read [AGENTS.md](../../AGENTS.md) before you start, especially the **Guides** and **File editing and formatting** sections.

## Why

The `jaytwo.Ergonomics.Pdf` test project uses `CliProcessSession` in three ways the guides do not cover well:

1. **Binary stdout.** `qpdf --show-attachment` writes raw bytes to stdout. The one-shot API only returns decoded text, so the consumer started a session and copied `StandardOutput.BaseStream`. That is the right approach. But it never closed stdin, and a session always opens a stdin pipe, so a tool that reads stdin would hang until the 600-second limit. No guide shows the pattern.
2. **Redundant `Terminate()` before `Dispose()`.** The consumer calls `Terminate()` and then `Dispose()` (and `Terminate()` inside a `using`) in two places. `Dispose()` already terminates the tree, so the guide's Lifecycle text is not getting that across.
3. **Unbounded startup handshake.** A test reads a `READY <port>` line from a server with no deadline, so a stalled server hangs the test. The guide's per-request timeout pattern also works here, but the guide does not say so.

## Relationship to other tasks

[session-wait-for-exit.md](session-wait-for-exit.md) also edits `talk-to-a-long-lived-worker.md`. It adds a "Stop a worker gracefully" subsection and replaces the first "Things to know" bullet. **This task must not add or change either of those.** The anchors below do not overlap with that task, so the two can land in either order. Every anchor must match exactly once. If one does not, stop and report it; do not guess.

This task uses only APIs that exist today. Do not use `WaitForExit(TimeSpan)` even if it exists by the time you start.

## Line endings and encoding

| File | Line endings | Note |
| --- | --- | --- |
| `docs/guides/capture-output.md` | CRLF | |
| `docs/guides/talk-to-a-long-lived-worker.md` | CRLF | |
| `docs/guides/README.md` | CRLF | |
| `test/jaytwo.Subprocess.UnitTests/Guides/CaptureOutputGuideTests.cs` | CRLF | Already contains non-ASCII text (an accented word in the encoding test). Keep the file UTF-8 and do not touch that text. |

Keep each file's line endings. Write ASCII in everything you add.

## Change 1: "Capture binary stdout" in `capture-output.md`

### Guide

In `docs/guides/capture-output.md`, find this exact line:

```markdown
This bound prevents an unbounded wait after exit or termination.
```

Insert the following after it, with one blank line before `## Capture binary stdout`. The existing `## Things to know` heading must still follow, with one blank line between.

````markdown
## Capture binary stdout

`StandardOutput` on a result is always decoded text, so a one-shot run cannot return raw bytes. For a tool that writes binary content to stdout, start a session and copy its raw stream. See [Talk to a long-lived worker](talk-to-a-long-lived-worker.md) for the session API.

<!-- sample: capture-binary -->
```csharp
using var session = new CliCommandBuilder("node", "-e")
    .WithArgument("process.stdout.write(Buffer.from([0, 1, 2, 254, 255]))")
    .GetCommand()
    .Start();
session.StandardInput.Dispose();   // the tool does not read stdin: close it so it cannot block on it

using var timeout = new CancellationTokenSource(runTimeout);
using var registration = timeout.Token.Register(session.Terminate);

using var bytes = new MemoryStream();
await session.StandardOutput.BaseStream.CopyToAsync(bytes);   // raw bytes; never also read StandardOutput as text
int exitCode = await session.WaitForExitAsync();
bool timedOut = timeout.IsCancellationRequested;
```

Namespaces: `jaytwo.Subprocess`, `System.IO`, `System.Threading`. `runTimeout` is your limit for the whole run (for example `TimeSpan.FromMinutes(2)`).

- A session always opens a stdin pipe. Close it right after start unless you are writing input. Otherwise a tool that reads stdin, or waits for it to close, blocks until it is terminated.
- `Start` rejects one-shot settings such as `Timeout` and `ExpectedExitCodes`. Bound the run with `Terminate` as shown and judge `exitCode` yourself.
- Read either `StandardOutput` or `StandardOutput.BaseStream`, never both. The text reader buffers bytes ahead of what it returns.
- `session.StandardError` holds the last 64K characters of stderr, for failure messages.
````

### Guide test

In `test/jaytwo.Subprocess.UnitTests/Guides/CaptureOutputGuideTests.cs`:

**Step A.** Replace the using block at the top of the file:

```csharp
using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;
```

with this (alphabetical order is required by StyleCop):

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
```

**Step B.** Add this test as the **last** method in the class, after `bound_an_incomplete_drain`. It is shown with its real indentation: 4 spaces before `[Fact]`, 8 spaces inside the method.

```csharp
    [Fact]
    public async Task capture_binary_stdout()
    {
        // arrange
        var runTimeout = TimeSpan.FromSeconds(30);

        // act
        // sample: capture-binary
        using var session = new CliCommandBuilder("node", "-e")
            .WithArgument("process.stdout.write(Buffer.from([0, 1, 2, 254, 255]))")
            .GetCommand()
            .Start();
        session.StandardInput.Dispose();   // the tool does not read stdin: close it so it cannot block on it

        using var timeout = new CancellationTokenSource(runTimeout);
        using var registration = timeout.Token.Register(session.Terminate);

        using var bytes = new MemoryStream();
        await session.StandardOutput.BaseStream.CopyToAsync(bytes);   // raw bytes; never also read StandardOutput as text
        int exitCode = await session.WaitForExitAsync();
        bool timedOut = timeout.IsCancellationRequested;
        // end sample

        // assert
        Assert.Equal(new byte[] { 0, 1, 2, 254, 255 }, bytes.ToArray());
        Assert.Equal(0, exitCode);
        Assert.False(timedOut);
    }
```

The lines between `// sample: capture-binary` and `// end sample` must match the guide fence line for line after trimming. Copy them; do not retype them.

### Guides index

In `docs/guides/README.md`, replace this row:

```markdown
| Decode stdout/stderr and spot incomplete capture | [Capture output](capture-output.md) |
```

with:

```markdown
| Decode stdout/stderr, spot incomplete capture, or capture binary stdout | [Capture output](capture-output.md) |
```

## Change 2: say plainly that `Dispose()` is enough

In `docs/guides/talk-to-a-long-lived-worker.md`, replace this exact paragraph (it is one line):

```markdown
`Dispose()` terminates the whole tree if it is still running and releases pipes and the stderr drain. Do not use `StandardInput` / `StandardOutput` after dispose.
```

with:

```markdown
`Dispose()` terminates the whole tree if it is still running and releases pipes and the stderr drain. Do not use `StandardInput` / `StandardOutput` after dispose. A `using` declaration is therefore enough on its own: you do not need `Terminate()` before `Dispose()`. Call `Terminate()` only when the process must be gone earlier, for example to unblock a pending read or before waiting for the exit code.
```

There is no sample change and no test change for this.

## Change 3: bound the startup handshake

In `docs/guides/talk-to-a-long-lived-worker.md`, find this exact line in **Per-request timeout**:

```markdown
`requestTimeout` is your per-request deadline (for example `TimeSpan.FromSeconds(30)`).
```

Add this paragraph directly after it, with one blank line between:

```markdown
Use the same pattern for a startup handshake, such as a worker that prints `READY <port>` once it is listening. A first read with no deadline hangs forever if the worker stalls before writing that line.
```

There is no sample change and no test change for this.

## Do not

- Do not change library code under `src/`.
- Do not change any existing sample fence or any existing `// sample:` region.
- Do not add the "Stop a worker gracefully" subsection or change the "Things to know" bullets in `talk-to-a-long-lived-worker.md` (the other task owns them).
- Do not edit `docs/charter.md`, `docs/scope.md`, `AGENTS.md`, or `README.md`.
- Do not change line endings or re-encode files.

## Steps

1. Make Change 1 (guide, guide test, index).
2. Make Change 2.
3. Make Change 3.
4. Run `dotnet build` from the repo root. Fix any StyleCop warning in the code you added (it should have none).
5. Run the tests below.

## Verify

`node` must be on `PATH`. From the repo root:

```sh
dotnet test test/jaytwo.Subprocess.UnitTests --filter "FullyQualifiedName~Guides"
dotnet test test/jaytwo.Subprocess.UnitTests
```

`GuideSampleTests` must pass, since it checks that the `capture-binary` fence and test region match. On Windows the test project also runs `net48`.

## Done when

- `capture_binary_stdout` and `GuideSampleTests` pass on every test target, and no existing test fails.
- `git diff --stat` lists only `docs/guides/capture-output.md`, `docs/guides/README.md`, `docs/guides/talk-to-a-long-lived-worker.md`, and `test/jaytwo.Subprocess.UnitTests/Guides/CaptureOutputGuideTests.cs` (plus this task file, if you mark it done).
- No file shows a whole-file rewrite in `git diff` (a sign of changed line endings).
