# Templates Architecture

Status: Current proposed architecture — pending team approval.

Templates Architecture covers template-based `func init` and `func new`. It is not approved for implementation or release. The [Phase 6 document](CLI-v5-Templates-Phase-6-Semifinal-Architecture.md) contains the detailed proposed design; older documents are historical or describe deferred work.

## Authority and reading order

Start here, then read Phase 6's [document status and requirements](CLI-v5-Templates-Phase-6-Semifinal-Architecture.md#document-status-and-requirements). Phase 6 alone owns the **proposed initial implementation requirements**. Its historical filename is retained for provenance; the architecture name is **Templates Architecture**.

Current source and tests establish what is implemented. Test assertions are not evidence of runtime execution without an actual execution record. Older designs, specifications and task lists preserve historical requirements and decisions; they do not independently authorize superseded architecture. Read them only after the Phase 6 dispositions.

## Existing infrastructure and missing integration

The retained Microsoft.TemplateEngine integration in [src/Func/Templates/Engine](../../src/Func/Templates/Engine) provides a synchronous factory/session, host context, installed-template listing and catalog/parameter projection. Declaration-reader, matcher and evaluator helpers exist, but constraint-aware command selection and invocation are not wired. [Engine tests](../../test/Func.Tests/Templates/Engine) describe isolated helper/listing coverage, not an executed project-plus-function journey.

[NewCommand](../../src/Func/Commands/NewCommand.cs) retains positional `[path]` and `--format`; its migration handler reports scaffolding and listing unavailable with a nonzero exit code before target writes. First-run setup can complete before that handler is reached. [InitCommand](../../src/Func/Commands/InitCommand.cs) still uses the six [Stack workload initializers](../../src/Workloads/Stacks), including existing adoption/healing. [IProjectStack](../../src/Abstractions/Projects/IProjectStack.cs) and [InstalledProjectStackCatalog](../../src/Func/Projects/InstalledProjectStackCatalog.cs) are additive metadata infrastructure, not a command migration.

Command-facing resolution, engine-evaluated parameter handling, exact session-bound invocation, guarded publication and production workload-to-hive registration are missing. The .NET initializer's separate `dotnet new` hive and test-only local installs are not that production registration. See the proposed [workload registration](CLI-v5-Templates-Phase-6-Semifinal-Architecture.md#workload-registration) and [real project-plus-parameterized-function proof](CLI-v5-Templates-Phase-6-Semifinal-Architecture.md#end-to-end-proof).

## Open decisions and deferred work

The [owner decision list](CLI-v5-Templates-Phase-6-Semifinal-Architecture.md#team-review-and-owner-decisions) remains open: Fabio/CLI owners must settle internal signatures, parameter-feature scope, token handoff and alias/variant policy; Sarah/Fabio must approve real content, package location, registration timing/retry and integrity; Francisco/team must approve supported-Stack/GA scope, force/profile/hidden-ID policy and public activation.

Future package lifecycle, broad Stack migration, template-first/multi-project quickstarts, producer pipeline expansion, general actions/bind sources/search and physical engine extraction are **deferred**, not initial implementation prerequisites unless explicitly approved. Preserve existing supported v4 behavior as a compatibility requirement; neither historical evidence nor a two-template proof establishes release parity. Keep working initialization and quickstart routes until separately authorized replacements are qualified.

## Historical navigation

- [Execution plan](CLI-v5-Templates-Engine-Redesign-Execution-Plan-2026-10-30.md) and [object-model draft](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md): October 30 checkpoints, not current tasks or mandatory APIs.
- [Frozen Phase 4 predecessor](CLI-v5-Templates-Fabio-First-Object-Model-Proposal.md): preserved proposal; Phase 6 is the current proposed contract.
- [Architecture Reset](CLI-v5-Templates-Architecture-Reset-Clean.md) and [Working Design](CLI-v5-Templates-Working-Design.md): historical research/design, including unavailable research links. Do not recreate their retired provider hierarchy, process-global inventory, project-facts model, prepared-operation lifecycle or extraction assembly.
- [Earlier program inventory](../templating-system/proposal.md): historical child specifications and checklists; Phase 6's document status and requirements determine what is retained, superseded or deferred.

## Authorization gates

Phase 6 and the Phase A cleanup passed independent Principal review. This editorial reconciliation precedes Francisco's separately authorized engineering-editorial/EER review and documentation checkpoint. Phase 6's [implementation sequencing](CLI-v5-Templates-Phase-6-Semifinal-Architecture.md#implementation-sequencing) describes future work only; even internal proof tests require separate explicit authorization. Fabio/Sarah/team approval and Functions Host Runtime qualification remain separate gates before public command activation or release.
