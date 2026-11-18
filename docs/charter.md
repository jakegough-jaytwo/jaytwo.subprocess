# Project Charter

Make invoking external CLIs from .NET straightforward, dependable, and hard to get wrong for the way most callers use subprocesses: run a tool, get decoded stdout/stderr, and a clear outcome.

**Small over broad.** This package does not aim to replace `System.Diagnostics.Process` for every scenario, or to compete with CliWrap on feature breadth. It prioritizes a deliberate, coherent API that covers the common path exceptionally well.

**Reliability over knobs.** The value is in the failure modes people get wrong by hand: draining stdout and stderr concurrently, terminating the process tree on timeout or cancel, bounding waits when pipes stay open, quoting arguments safely, and keeping secrets out of diagnostics.

_The goal is functionality that remains simple to reason about, maintain, and use correctly._

## Product philosophy

The default story is one-shot execution: build a command, execute it, inspect a result. Optional features stay off that path until needed. The public surface should stay readable for someone who only wants "run this and give me the strings."

`jaytwo.Subprocess.Streaming` is an explicit escape hatch for long-lived workers. It owns the process lifetime (start, open pipes, bounded waits, graceful or forced stop, tree termination) and leaves the conversation (what is written and read, and when) to the caller. It is not a second product line.

Prefer capabilities whose complexity stays contained and whose interactions are predictable. A feature that only exists so callers never leave the library, or that makes the default builder harder to read, is generally contrary to the purpose, even when the functionality itself seems useful.

**The library should stay simple even as it becomes capable.**

## Architectural principles

1. **Managed .NET.** No native code and no new runtime dependencies without a demonstrated need that existing facilities cannot meet.
2. **Explicit over magical.** Prefer small overload families and obvious ownership (who disposes what) over inference or hidden behavior.
3. **Sync and async aligned.** Synchronous and asynchronous execution share the same semantics.
4. **Fail clearly.** Prefer typed outcomes (timeout, cancel, unexpected exit, failed termination) and refuse unsupported platforms up front rather than silently degrading (for example, never fall back to parent-only kill).
5. **Compatibility with honesty.** Follow the project's target-framework matrix; breaking behavior is called out and versioned deliberately.

These principles guide engineering judgment. Deviations need a concrete correctness, interoperability, or adoption reason, not "a caller asked for it."

## Product scope

What belongs in the package is recorded in [scope](scope.md). The philosophy above is the lens for deciding what goes on that list.

## Governance

Engineers and agents may make implementation decisions consistent with this charter and the adopted scope. Changes to the charter or scope, adding a capability, or adding a caller-visible limit or exclusion require project-owner approval.

Existing code, backlog items, and delivery priorities do not independently establish product commitments or justify departures from the charter.

The charter defines enduring direction. Capability boundaries belong in scope; how-tos belong in guides; coding practice belongs in [AGENTS.md](../AGENTS.md).
