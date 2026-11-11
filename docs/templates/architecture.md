# Templates Architecture

The [component model](Templates-EndToEnd-Object-Model-Review.md) is authoritative. PR #5872 establishes command-scoped discovery and exact selection only; creation and writing files belong to later integration work.

## Responsibilities

The CLI uses one Template Engine for project and item templates:

- `func init` consumes project templates.
- `func new` consumes item templates.
- Quickstarts are project templates, not a third template kind.

| Component-model responsibility | Owner |
| --- | --- |
| CLI Commands | Arguments, prompting, orchestration, diagnostics, and any later writing of files |
| Project and Stack Information | Project resolver and owning Stack/workload |
| Template Engine | Session, one installed inventory, metadata projection, selection policy, and engine constraints |
| Installed Templates | Registered content discovered by the command-scoped `TemplatePackageManager` |
| Generate Files Privately | Later integration responsibility; not implemented by PR #5872 |
| Write Files to Project | Later integration responsibility; not implemented by PR #5872 |

These are responsibilities, not a required class-per-row design. Reuse existing types and utilities. Keep `Templater` thin: own one session, obtain one inventory, project minimal CLI metadata, retain exact candidates, and delegate resolution policy. Do not introduce a second engine, generalized task framework, global compatibility framework, or conversion-only adapter.

`TemplateSelection` is one internal operation, not a new public API or framework. It owns identity and alias matching, kind filtering, ambiguity, and result policy. Candidate eligibility is a separate concrete `TemplateEligibilityEvaluator` because source-backed validation and engine constraint evaluation do not belong in matching. The evaluator returns a candidate-scoped result: eligible with no diagnostics, or rejected with exactly one diagnostic for that candidate. `TemplateSelection` immediately consumes that result; `Templater` directly consumes the selection operation. The factory composes both with one session. No interface or second inventory is introduced.

## Installed inventory and command session

Workload installation registers immutable, versioned content before commands discover it. The command-scoped `TemplatePackageManager` inventory is the discovery source. There is one inventory per command session; `Templater` does not rescan after selection or refresh aliases. Content servicing and cache re-registration happen outside the command session.

`TemplateEngineSession` owns `EngineEnvironmentSettings`, `TemplatePackageManager`, a lazily created `TemplateConstraintManager`, and one cached runtime-read-only package/template snapshot. The snapshot copies only the collection containers, preserving the original package and `ITemplateInfo` references. `Templater` owns that session, projects the returned candidates to `TemplateCatalogEntry`, and retains the original candidate references and their lifetime. `TemplateSelection` consumes that inventory and projection without source I/O, creating another inventory, or refreshing it. A selection must resolve to the exact candidate issued by the same live session; it is not reconstructed from catalog fields or looked up again by alias.

Workload installation owns package acquisition and Hive registration. Discovery never downloads or installs content. The Template Engine remains in Core Tools for this milestone; physical extraction under issue #5330 is separate work.

## Listing, identity, and selection

Listing includes hidden templates and templates whose Functions-specific or required template metadata is unusable, with diagnostics retained on the projected entry. Resolution rejects hidden or invalid candidates. Listing is not a guarantee that every provider was fully enumerated.

`TemplateSelection` applies this policy to the inventory supplied by `Templater`:

1. Match a full identity before aliases. If the returned inventory contains multiple entries with one identity, resolution fails.
2. Reject a full identity that is the wrong template kind or ineligible; never substitute a related template.
3. Match aliases only against templates that declare them. Two or more eligible candidates remain ambiguous; do not choose the first candidate or expand to siblings.
4. Return exactly one candidate when selection succeeds. Every unsuccessful outcome has zero candidates.

`TemplateResolutionResult` contains only `Candidates` and `Diagnostics`: zero candidates means unsuccessful, one means selected, and two or more means ambiguous. `TemplateCatalogEntry` retains the exact engine candidate and a non-owning owner token while snapshotting caller-owned aliases, parameters, and nested parameter choices. A live owning session accepts an immutable same-session copy that preserves its token and candidate; foreign, unbound, forged, or disposed-session entries are rejected. Failure details such as template identity, package, name, kind, source, and cause are structured diagnostics rather than unsuccessful result candidates.

`groupIdentity` describes related variants; it does not determine an alias result or select a variant. A template with no group is not an implicit family. The shared `shortName` alias of two variants may therefore be ambiguous. Project and item templates remain distinct through `tags.type`. Templates require at least one non-empty `shortName`: the engine may omit a template with no value, while projection filters empty aliases and marks an exposed candidate with no usable alias as invalid.

A command may request candidate metadata, own the prompt, and then resolve the exact chosen entry. PR #5872 does not add a no-reference prompting API; command prompting remains with the consuming command, including the downstream PR #5878 integration.

## Eligibility and constraints

`TemplateEligibilityEvaluator` uses `TemplateConstraintManager` for engine constraints. It validates raw declarations before normalization can hide malformed or duplicate constraints, reads the candidate configuration through the existing session, and verifies that the engine exposes exactly one matching factory. Unavailable, restricted, unknown, or unevaluated required constraints reject only the affected candidate with a structured diagnostic. Cancellation propagates.

The session lazily creates one `TemplateConstraintManager` when engine constraints need evaluation. The narrow `func-bundle` constraint is evaluated against bundle context already attached to the session, using the lazy `FuncTemplateConstraintEvaluator`. A `func-workload` constraint fails closed until an authorized workload inventory is available. PR #5872 does not perform project Stack or language compatibility. The downstream PR #5878 command consumes `ITemplaterFactory`; project interpretation and compatibility remain owned by the Stack. The engine session may carry project context, but selection does not use it to filter candidates.

For constraints whose source configuration cannot be read or verified, resolution fails for that candidate. Additional configuration files are rejected when merged constraints cannot be proven. Listing propagates a surfaced inventory exception unchanged; resolution returns surfaced inventory failures as diagnostic causes.

The inventory may not expose every conflicting registration as a distinct candidate. This layer does not scan mounts again to detect duplicate registrations; immutable, versioned workload content must avoid identity conflicts. Some Template Engine providers may log or suppress enumeration errors and return a partial inventory. There is no typed completeness result available to this layer. It does not parse logs or introduce a second scanner, so a zero-candidate result with a `not-found` diagnostic does not prove that the inventory was complete.

## Later command integration

PR #5872 does not activate `func init` or `func new`, replace existing Stack initializers, or prompt when no template reference is supplied. Creation and writing files are later integration work. Later integration must preserve command-owned orchestration and diagnostics, Stack-owned project interpretation and compatibility, workload-owned content registration, and template-defined inputs and tasks.

This discovery/selection change defines no generation or file-writing mechanics. Those contracts require separate accepted architecture and authorization.

End-to-end qualification must cover workload registration, project compatibility, real `func init` and `func new` journeys, host discovery, and supported platforms. These are separate integration gates; this discovery-and-selection design does not generalize them into a single framework.

## References

- [Component model](Templates-EndToEnd-Object-Model-Review.md)
- [Implementation](implementation.md)
- `src/Func/Templates/Engine/TemplateEngineSession.cs`
- `src/Func/Templates/Engine/Templater.cs`
- `src/Func/Templates/Engine/TemplateSelection.cs`
