# First-run experience

> **Status: draft.** This documents the intended behaviour of the v5 first-run
> prompt and the `func setup` flow it drives. Some items are implemented today,
> some are tracked as follow-up work. See "Implementation status" at the bottom.

## Goals

- New users get a guided path to a working CLI without reading docs first.
- Existing users (already ran `func setup` or `func workload install`) are
  never prompted again.
- Power users and CI runs can always opt out non-interactively.

## State

The first-run signal lives entirely under the user's func home directory
(default `~/.azure-functions/`, overridable via `FUNC_HOME`).

- Marker file: `~/.azure-functions/.first-run-complete`. Presence is the
  signal; the file's contents are informational only (an ISO timestamp).
- Workload registry: `~/.azure-functions/workloads.json`, read via
  `IWorkloadStore`.

A user is considered "first run" when **both**:

1. The marker file does not exist, **and**
2. `IWorkloadStore.GetWorkloadsAsync()` returns zero entries.

The workload check covers users who set up the CLI before the marker was
introduced, or who installed workloads directly with `func workload install`.

## When the prompt fires

The prompt fires on the first invocation of any command, **including bare
`func`** (no args). It is skipped for:

- `func setup`, `func workload …`
- `func help`, `func version`, `func --help`, `func --version`
- Non-interactive contexts (no TTY, `--non-interactive`, JSON output mode, CI)
- Invocations that hit a parse error (let the parser's error surface first)

## Prompt copy

```
Looks like this is your first time running func.

The new CLI uses installable workloads to bring in language stacks
(Node.js, Python, .NET, Go) and the host runtime. Running `func setup`
now will install the host and the dev environment / dependencies for
the stack(s) you pick.

You can always add more later with `func setup --features <node|dotnet|go|python>`.

Run `func setup` now? [Y/n]
```

Behaviour:

| Input | Effect |
|---|---|
| **Yes** (default) | Run `SetupRunner` inline, refresh the workload loader, continue with the original command. |
| **No** | Write the marker. Continue with the original command. Never re-ask. |
| **Ctrl+C** | Write the marker. Abort the command. Treat as "stop pestering me". |

## `func setup` behaviour

### Interactive

- Multi-select shows all stacks (`node`, `python`, `dotnet`, `go`).
- Stacks already installed are **disabled and grayed out**, with an
  `(installed)` suffix. They cannot be re-toggled.
- ENTER with nothing selected is a clean opt-out: exit 0, marker written,
  hint shown ("No stacks selected").

### Non-interactive

- Installs the default `runtime` feature set (host + extension bundle).
- No prompts. Output respects `--output-mode plain|json`.

### Marker write rules

`func setup` writes the marker on every terminal outcome that did **not**
fail mid-install:

- Success (all selected workloads installed)
- Opt-out (empty selection)
- Partial install with `--check` (no install attempted)

A real install failure leaves the marker unwritten so the user is
re-prompted next time.

## `func init` and `func new` interaction

These commands trigger the first-run prompt with two refinements:

1. **Skip the prompt if any workloads are already installed.** Re-uses the
   same "workloads present ⇒ not first run" check, so existing users go
   straight to init.
2. **After setup succeeds inside an init/new flow, refresh the workload
   loader** before resuming. The loader currently snapshots `workloads.json`
   at host build time; we need an explicit reload hook on `IWorkloadStore` /
   the loader registration so the freshly installed stack is visible to the
   same process that will run init.

Without (2), `func init` would have to relaunch the CLI to pick up the new
workload, which is a worse UX than just telling the user "setup done,
re-run `func init`".

## Subsequent-run breadcrumb

When the marker is present **and** no workloads are installed (i.e. the
user said "No" first time, or installed nothing), surface a single muted
hint on each command:

```
Tip: run `func setup` to install your dev environment and language stack
dependencies.
```

Rules:

- One line, muted style.
- Suppressed in JSON output and non-interactive contexts.
- Not a prompt, not a blocker.
- Suppressed for the same skip-list as the first-run prompt
  (`func setup`, `func help`, `func version`, …).

## Out of scope

- Telemetry / consent prompts (separate flow).
- Per-project first-run (this is per-user / per-`$FUNC_HOME`).
- Auto-running `setup` without confirmation.
- A way to re-trigger the prompt. Users can run `func setup` directly.
- "No" answer expiring after N days. Sticky forever.

## Implementation status

| Item | Status |
|---|---|
| Marker at `~/.azure-functions/.first-run-complete` | Done |
| Workload-aware first-run check | Done (PR #5220) |
| Marker written on `func setup` success and opt-out | Done (PR #5220) |
| Multi-select labels installed stacks with `(installed)` | Done (PR #5220) |
| Empty selection exits cleanly | Done (PR #5220) |
| Bare `func` triggers the prompt | **Pending** |
| New, longer prompt copy with `--features` hint | **Pending** |
| Ctrl+C writes the marker | **Pending** |
| Already-installed stacks are disabled (not just labelled) | **Pending** |
| `func init` / `func new` trigger prompt + workload loader reload | **Pending** |
| Subsequent-run breadcrumb | **Pending** |

Pending items are tracked in the follow-up implementation PR.
