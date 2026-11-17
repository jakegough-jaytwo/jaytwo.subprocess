# Agent Instructions

## Authority

Product behavior: the [charter](docs/charter.md) governs, then [scope](docs/scope.md). This file governs how agents work. README, guides, code comments, and task notes are not product authority.

- If two product documents disagree, the higher one wins. Tell the owner about the difference instead of resolving it quietly.
- Ask the owner before changing the charter, scope, or this file; before adding a capability; or before adding a caller-visible limit or exclusion.
- Prefer the smallest change that meets the request and stays inside scope. If a request only stops someone from using `Process` or another library, put it in Streaming or decline; do not widen the one-shot API by default.
- Do not add native code or new runtime dependencies without explaining why existing facilities are insufficient and getting owner approval.

## Workflow

- Do not ask for permission before read-only exploration inside the repo. It is fine to inspect context with commands like `ls`, `grep`, `find`, `rg`, `Get-Content`, and `git diff`.
- Prefer read-only inspection first when gathering context.
- Only stop to ask before destructive actions, network access, or commands that require escalation outside the sandbox.
- Prefer minimal patches over full-file rewrites.

## File editing and formatting

- Preserve a file's existing line endings when editing.
- Honor `.editorconfig`, especially `end_of_line` and `insert_final_newline`, when present.
- If `end_of_line` is not specified, detect the file's dominant existing line ending and keep it.
- Do not introduce mixed line endings within a file.
- Do not normalize or rewrite line endings unless explicitly requested.
- Prefer ASCII in source and docs unless a file already intentionally uses non-ASCII characters. Avoid introducing typographic punctuation that can turn into mojibake.
- Keep comments sparse and practical. Short "why" comments are fine; avoid tutorial-style narration.

## Code style and structure

- Match the existing public API style: prefer small overload families that forward to one core implementation instead of collapsing everything into one clever method.
- Stay explicit rather than magical. This library favors ergonomic helpers, not inference or hidden behavior.
- Use expression-bodied members for trivial pass-through wrappers; use block bodies when there is meaningful control flow, disposal, logging, or validation.
- When logic is only relevant to one method, prefer a local helper or local function near the call site rather than extracting a broader private method.
- Use file-scoped namespace declarations (`namespace jaytwo.Ergonomics.Ado;`), not block-scoped.
- Prefix private instance fields with underscore: `_connectionFactory`, `_inner`, `_logger`.
- Mark concrete implementation classes `internal` when they are not part of the intended public API surface.
- Preserve nullable intent. Keep `?`, null guards, and `ArgumentNullException` checks consistent with surrounding code.
- Guard constructor parameters with the null-coalescing throw inline: `_x = x ?? throw new ArgumentNullException(nameof(x));`, not a separate `if` block.

## API design

- Reuse the repo's naming patterns for delegate parameters and overloads.
- Prefer `ValueTask` and `ValueTask<T>` over `Task` and `Task<T>` for async methods that frequently complete synchronously or return lightweight results.

## Resource management and async

- Prefer explicit ownership and cleanup. If code creates a disposable object, make disposal behavior obvious and keep failure cleanup close to creation.
- In factory/open methods, wrap post-creation work in `try { ... } catch { resource.Dispose(); throw; }` so callers never receive a partially initialized resource.
- Prefer `finally` in `Dispose()` when the second cleanup must run even if the first throws.
- In library async code, continue using `ConfigureAwait(false)`.

## Tests

- Mirror the existing xUnit style: clear arrange/act/assert sections, descriptive behavior-oriented names, and separate sync/async coverage instead of hiding cases behind heavy abstraction.
- Name test methods as `MethodName_does_behavior`, not `MethodName_WhenCondition_ThenOutcome`.
- Use lowercase `// arrange`, `// act`, `// assert` section comments in every test method. Never omit a section - if a section has nothing to do, add a parenthetical note, e.g. `// arrange (no setup needed)` or `// act (no action needed)`. Always extract act calls into named variables; do not call the method under test inline inside an `Assert`.

## Repo map

- `src/jaytwo.Subprocess`: the library (one-shot execution plus `Streaming` escape hatch).
- `test/`: unit tests, including guide sample honesty tests under `Guides/`.
- `docs/`: [charter](docs/charter.md), [scope](docs/scope.md), and usage [guides](docs/guides/README.md).

## Guides

`docs/guides/` how-tos are kept honest by tests in `test/jaytwo.Subprocess.UnitTests/Guides/`. Guides are not product authority.

- Every C# fence in a guide that is a real sample must be preceded by `<!-- sample: id -->`. The same code must appear in a guide test between `// sample: id` and `// end sample`. `GuideSampleTests` fails when they differ (comparison is trimmed-line).
- Change the guide and the matching test region together. Do not edit one without the other.
- Prefer samples that run with `node` (via `TestWorkspace` scripts or `-e`) so CI does not need extra tools. Put paths, tokens and other placeholders in arrange; keep only the caller-facing API inside the sample region.
- Do not put `using` directives inside a sample region (they are not valid inside a test method). Name required namespaces in the guide prose next to the sample when helpful.
- When you add a new guide or a new sample, add a running test for it and confirm `GuideSampleTests` still passes.
