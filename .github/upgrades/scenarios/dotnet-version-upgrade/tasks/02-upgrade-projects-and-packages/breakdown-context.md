## Detected Hints

### hint: multi-project-dependency-ordering
- **Status**: resolved (overridden)
- **Priority**: MUST (nominal)
- **Evidence**: 28 projects with dependency chain (Abstractions <- Func <- Func.Tests; Abstractions <- workloads <- tests).
- **Detected**: 2026-11-17, during task 02-upgrade-projects-and-packages
- **Resolution**: Overridden by the user-selected All-At-Once strategy (strategies/all-at-once.md §3-5: "no tier ordering"; anti-pattern "Don't introduce tier ordering") and the scenario-instructions Execution Constraint "Single atomic upgrade: all project TFMs and central package pins change together in one task; no tier ordering." Tiering is also impractical under CPM with transitive pinning: shared central pins would create mixed-TFM states.

### hint: large-package-replacement-batch
- **Status**: not fired
- **Evidence**: 0 incompatible packages; only version bumps.

## Breakdown Decisions

### task: 02-upgrade-projects-and-packages
- Not broken down (atomic). Strategy precedence (user-selected all-at-once + scenario execution constraint) outranks the generic dependency-ordering hint. Work is mechanical (literal TFM replace, LangVersion, ~12 pin bumps), all projects Low difficulty, 0 binary-incompatible APIs, 22 source-incompatible overload rebinds. A concern split (TFM -> packages -> fixes) would have no independently verifiable done state, because the build stays broken until all three are done.
- Extension TaskBreakdown rules: none apply.
