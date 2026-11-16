# Guides

Short how-tos for ordinary tasks with `jaytwo.Subprocess`. Guides describe usage; the [README](../../README.md) is the behavior reference for capture, termination and outcomes.

| Task | Guide |
| --- | --- |
| Run a tool and read its exit code and output | [Run a command](run-a-command.md) |
| Build a safely quoted argument list | [Pass arguments](pass-arguments.md) |
| Pipe text or bytes into stdin | [Feed standard input](feed-standard-input.md) |
| Decode stdout/stderr and spot incomplete capture | [Capture output](capture-output.md) |
| Bound runtime, cancel a run, accept non-zero exits | [Timeouts, cancellation and exit codes](timeouts-cancellation-and-exit-codes.md) |
| Keep passwords out of logs and `ToString` | [Mask secrets](mask-secrets.md) |
| Pick a different launch line per OS | [Run across platforms](run-across-platforms.md) |
| Keep a worker process alive and talk to it live | [Talk to a long-lived worker](talk-to-a-long-lived-worker.md) |

## Conventions

- Samples use placeholders such as `workingDirectory` and `token` for values you supply. Every guide sample is a running test; see below.
- Prefer `CliCommandBuilder` then `GetCommand()`, then `Execute` / `ExecuteAsync`. For a process you keep alive, use `jaytwo.Subprocess.Streaming.CliProcessSession` instead.
- By default an unexpected exit code, timeout or failed termination throws a `SubprocessException` subclass that carries the `CliCommandResult`. Cancellation always throws and is never swallowed by `suppressCommandLineExceptions`.

## Keeping guides honest

Every C# sample here is a running test. A sample is marked `<!-- sample: id -->` in the guide and `// sample: id` ... `// end sample` in [test/jaytwo.Subprocess.UnitTests/Guides/](../../test/jaytwo.Subprocess.UnitTests/Guides/), and `GuideSampleTests` fails when the two differ. Change both together.
