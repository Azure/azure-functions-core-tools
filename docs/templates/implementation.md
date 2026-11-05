# Implementing the Template Engine

**Status:** Incremental implementation plan. The [architecture](architecture.md) is the normative description of the Template Engine.

This document distinguishes the PR 1 foundation from deferred rendering, command integration, and qualification. PR 1 does not require the complete `func init` / `func new` / Functions Host journey.

## Existing committed infrastructure

The existing object model includes `ITemplaterFactory`, `TemplateEngineContext`, `TemplateEngineSession`, `Templater`, `TemplateCatalogEntry`, and `TemplateParameterDefinition`. The host registers the factory, and the engine session uses the dedicated CLI template Hive. Catalog and parameter projectors, func-host metadata readers, and constraint helpers provide reusable metadata handling. Commands do not yet call the factory.

## PR 1 foundation

The foundation provides a command-facing creation context, factory, command-scoped `Templater`, installed catalog metadata, and exact session-bound selection with actionable diagnostics. `TemplateEngineSession` remains internal to `Templater`. The implementation reuses `TemplateEngineContext`, `TemplateCatalogEntry`, and existing metadata/constraint helpers; it adds no group model or general task framework. The first production-content scope and its remaining identity/version decisions are defined in the [Production Template Content Contract](architecture.md#production-template-content-contract).

PR 1 does not change `InitCommand` or `NewCommand`, activate template listing or generation, or replace existing Stack initializers.

## Deferred work

- `TemplateEngineSession` and `Templater` use the dedicated CLI template Hive. Local test packages can be registered there; production workload registration into that Hive is still required. Production package identity, template identities, and version coordination remain owner decisions.
- A fixture-specific private-rendering prototype has been developed separately, including bounded source/output handling and planned output. It is not part of this PR, is not wired through the normal factory or a command, and has not been built or tested.
- Installed-content snapshot verification, general supported parameter processing, Stack-owned task contributions, and command-integrated publication and reporting remain later work.
- `InitCommand` and its six existing Stack initializers continue to handle project creation, adoption, and configuration repair until replacements qualify.
- `NewCommand` retains its existing public argument surface. Its current migration handler reports listing and scaffolding unavailable before changing the target; PR 1 does not alter that behavior.
- Quickstarts are project templates in the approved architecture. The existing Quickstart route remains until its migration is implemented and qualified.
- The Template Engine remains in Core Tools for this milestone. Physical extraction under issue #5330 is deferred until after Ignite.

### Existing initialization limitations

The current `func init` path has known safety limitations: it can create the destination before validation, perform force cleanup before all Stack-specific checks, remove `.git` pointer files, and report success after some required configuration writes fail.

These behaviors belong to the existing implementation. The proposed template-based path must correct them before replacing an initializer.

Useful source entry points include `src/Func/Templates/Engine/`, `src/Func/Commands/InitCommand.cs`, `src/Func/Commands/NewCommand.cs`, `src/Abstractions/Projects/`, and `src/Workloads/Stacks/`. Current source and tests define the implemented compatibility behavior.

## Incremental delivery

1. **PR 1 — establish the object-model foundation.** Create `Templater` from the existing command context and factory; list installed template metadata; resolve one exact eligible selection or return diagnostics. Keep the engine session internal and leave commands unchanged.
2. **Connect installed content.** Register workload-provided packages through the shared CLI Hive contract and verify the selected source before invocation. Delivery mechanism must not change discovery behavior.
3. **Qualify private generation.** Use representative project and item content to validate exact invocation, supported parameter processing, bounded private output, cancellation, and approved template-defined tasks. Stack-specific tasks come from the owning workload; do not introduce a generic task framework.
4. **Add guarded publication.** Validate all intended destination effects, protect existing and Git metadata, preserve non-force behavior, and report partial effects truthfully.
5. **Integrate commands incrementally.** Preserve `func init` adoption/repair and current Stack behavior; add project-template and item-template paths without changing public grammar until each path is ready.
6. **Qualify production content and runtime behavior.** Verify production workload registration, Functions Host discovery, supported platforms, and the approved Stack scope before activating public paths.

These increments keep the first PR reviewable. The complete Node.js end-to-end journey is a later integration milestone, not a prerequisite for PR 1.

## Windows create-only file writing

`ProjectFileWriter` in `src/Func/Projects/FileWriting/` accepts a destination, a dictionary of relative file paths and immutable bytes, and explicit relative directory paths. It consumes planned bytes directly without depending on Microsoft.TemplateEngine or adding another rendering-result model. The writer is not registered or called by public commands; command integration remains separate.

The writer validates the complete plan before opening the destination. It rejects rooted and traversal paths, invalid components, device names, short-name aliases, Git metadata paths, duplicate normalized paths, and file/directory conflicts. Plans are limited to 1,024 entries including implicit parent directories, with at most 32 components per relative path. Preflight checks all reachable destinations before creating anything. Existing directories are preserved; existing files are never overwritten.

The Windows filesystem adapter accepts only drive mappings directly naming a physical `HarddiskVolume`, opens that captured device root rather than resolving the drive letter again, and verifies that the handle names a volume GUID root, not a subdirectory. Substituted, redirected, and network roots fail closed. Mapping changes are checked before and after effects; a retargeted drive alias cannot redirect handle-relative creation. Every physical ancestor from the volume root is retained.

The adapter opens each existing directory relative to its retained parent handle, rejects reparse points, case-sensitive directories, and name aliases, and verifies a local NTFS or ReFS volume. Directory handles request directory-list access and permit read sharing only, preventing ancestor rename and deletion while the session is active. Attribute-only opens do not provide that protection, and metadata-only handles can still change directory attributes. The adapter therefore rechecks the relevant pinned ancestors before and after creation and after writing and stream disposal. It does not repeatedly validate unrelated retained siblings, and it reuses one path buffer per session. A 128-sibling test asserts equal directory-validation call counts for the first and last creation; this is not a wall-clock performance benchmark.

Each new file or directory uses `NtCreateFile` with `FILE_CREATE`, a single-component name, the pinned parent handle, and no-reparse options. A destination that appears after preflight is a failure, not a replacement. A missing destination may be created, but its parent ancestry must already exist.

`ProjectWriteResult.Canceled` is an operation outcome independent of its effects. Cancellation before an effect leaves that effect `NotAttempted`; cancellation after a completed write preserves its `Created` status. A final cancellation check prevents cancellation after the last write from becoming success. A cancellation during a created-file write retains that effect's failure and cleanup history. Callers must inspect the overall result, not infer success solely from completed effects.

Expected filesystem failures return actual per-operation effects: created, preserved, failed, not attempted, removed after failure, cleanup pending, or uncertain. The original newly created file handle retains cleanup ownership while a separately owned duplicate is used for stream construction, writing, flushing, and disposal. Errors in those stages trigger cleanup through the original handle; an additional disposal error is retained as `CompletionError`. A failed safety check after directory creation likewise attempts cleanup of only that new directory.

Unexpected exceptions after file creation are rethrown unchanged after cleanup. The session adds the affected `ProjectWriteEffect` to the exception's `Data`; `ProjectFileWriter.WriteAsync` adds the complete partial `ProjectWriteResult`. Each is keyed by its respective type, and cleanup and additional disposal errors remain separate in the effect.

A successful disposition request sets `CleanupRequested`, but does not establish removal. The writer closes its owned handles before checking the entry relative to its pinned parent. `RemovedAfterFailure` requires observed absence. An entry still present or explicitly reported as delete-pending is `CleanupPending`; failed observation or failed cleanup is `Uncertain`, with the original error and separate `CleanupError`. Windows can report access denied while another handle delays deletion: that observation remains uncertain, not removed. Earlier completed files and created directories remain. This is not an all-or-nothing transaction, crash recovery, or a guarantee against later external changes.

Force, replacement, append, and command-specific authorization are not implemented. Their policies and changed-original guards remain separate integration work. Network destinations, drive roots, other filesystems, reparse-point ancestry, and unsupported directory semantics fail closed. Runtime qualification is bounded to the Windows architecture and volumes exercised by the focused tests; it does not qualify every Windows release, filesystem filter, or RID. **RUNTIME VALIDATION REQUIRED - UNIX ATOMIC NO-REPLACE.**

Principal-findings reconciliation used .NET SDK 10.0.403 and a Release build with CI analyzers enabled on Windows x64. The directly affected filter passed 33 tests before the complete writer suites passed 117 tests on ReFS and 117 on lane-private NTFS, with no failures or skips in those final runs. The subsequent B4 regression passed 12 tests; the focused writer suite passed 130 on ReFS and 130 on NTFS, also with no failures or skips. Native cases exercise collisions introduced after preflight, retained-handle rename protection, junction rejection, metadata-only reparse mutation, and retagging immediately before file and directory creation. Corrections also cover final-write cancellation, delayed deletion through a retained duplicate handle, stream construction/write/flush/disposal failures, and bounded directory validation. No live system drive mappings were created or retargeted: redirected-root and mapping-change cases use controlled root-opening and mapping-query seams. Live `SUBST`/DOS-device retargeting remains a runtime qualification gap. These tests do not activate or qualify public commands or the complete template journey.

## Stack behavior to preserve

The six existing initializers provide the compatibility baseline. The exact supported combinations and defaults must be checked against current source and tests before replacing an initializer.

| Stack | Current project languages and important options |
| --- | --- |
| .NET | C# and F#; `--target-framework` defaults to `net10.0` in the documented baseline. `net11.0` is a C# case; existing .NET templates use a separate `dotnet new` hive. |
| Node.js | JavaScript and TypeScript; bundle options and `--skip-npm-install` (default false). |
| Python | Python (`py` alias); bundle options and the current Python project scaffold. |
| Go | Go (`golang` alias; existing worker identifier `native`); bundle options and `--skip-go-mod-tidy` (default false). |
| Java | Java; bundle options and Maven-based scaffolding. |
| PowerShell | PowerShell (`pwsh`, `ps` aliases); bundle options, `--managed-dependencies` (default false), and `--runtime-version` (documented default 7.4, with 7.4/7.6 options). |

Common bundle options include `--no-bundles` and `--bundles-channel`. Preserve Stack/language alias handling, contributed defaults, interactive language selection for multi-language Stacks, and single-language configuration omission. The archived [Stack inventory](archive/func-init-execution/stack-inventory.md) provides detailed historical evidence; current source determines implemented behavior.

Keep the distinction between the language Stack and the Azure Functions Runtime. Do not interpret a worker alias or a public `--runtime-version` option as a new architecture term.

## Remaining implementation gates

| Area | Needed before |
| --- | --- |
| Production workload package registration | Production templates are available in the CLI Hive |
| Verified source snapshot and supported parameter semantics | Invoking selected installed content |
| Approved template-defined task contracts | Coordinating tasks contributed by a Stack |
| Guarded destination publication | Writing generated files to a project |
| Supported content, Functions Host, and platform qualification | Activating public template-backed command paths |

Engine work with isolated representative content does not need to wait for the entire production catalog. A public template-backed journey does need approved content and production registration.

## Focused tests and later acceptance

### PR 1 tests

PR 1 covers factory creation, catalog listing, exact identity selection, alias ambiguity, duplicate identities, ineligible candidates, selection ownership, invalid selection, and actionable diagnostics. The focused filter ran against an isolated snapshot of the exact staged candidate with .NET SDK 10.0.403: 108 passed, 0 failed, and 0 skipped.

### Later end-to-end journey

Use a real project template and a parameterized HTTP-function template for one supported Stack:

1. Install both into an isolated CLI hive.
2. Run `func init` and verify the resulting project with the normal project resolver.
3. Run `func new` against that project, passing a required parameter.
4. Verify the generated function uses the supplied parameter.
5. Start the approved Functions Host and verify actual function discovery and invocation.

A `host.json` file, successful listing, or basic text-file fixture alone does not establish this proof. One representative Stack does not establish GA coverage.

### Focused correctness cases

- **Discovery and selection:** missing content, multiple metadata variants, ambiguous aliases, ineligible exact IDs, duplicate identities, unavailable constraints, and no sibling substitution.
- **Parameters:** candidate-specific options before final selection; conditional required/disabled/default values; explicit empty and repeated values; interactive and noninteractive errors.
- **Content integrity:** changed or missing configuration/assets between selection and invocation; rendering from the verified selected content; unsupported engine features fail before destination effects.
- **`func init`:** all existing Stack options, adoption/repair, safe occupied non-project directories, write-if-missing behavior, complete-project refusal, force confirmation, `.git` protection, and required configuration failures.
- **`func new`:** project resolution, existing command grammar, template listing versus invocation, item collisions, guarded modifications, and no unrelated initialization configuration writes. Verify that invoking `func new` outside an initialized Functions project fails with actionable guidance to run `func init`, without initializing a project or modifying the destination.
- **Publication:** path/ancestor/case checks, guarded no-replace or replacement on supported operating systems, cancellation, cleanup, and truthful partial-effect reporting.
- **Packaging:** isolated test registration versus real workload registration; missing or incompatible installed content; retry and version coordination.

## Validation status and limitations

The latest Lane 3 Principal delta review passed with a nonblocking finding; that review was inspection-only. The focused PR 1 tests were executed against an isolated snapshot of the exact staged 13-file candidate with .NET SDK 10.0.403: 108 passed, 0 failed, and 0 skipped. An earlier attempt to validate from the lane runtime stopped because it lacked `dotnet.exe`; this publication run used the installed SDK. **RUNTIME VALIDATION REQUIRED** for production generation and host qualification.

Lane 4's Windows qualification is separate evidence and does not qualify this Lane 3 candidate. Focused unit tests do not establish installed-hive snapshot integration, general source integrity, guarded physical publication, cross-platform behavior, Functions Host discovery, or supported Stack coverage; these remain unqualified.

Keep current initializers and the existing Quickstart route until their replacements or migrations are implemented and qualified. Package install/update/uninstall/search, producer-pipeline expansion, a general task framework, and physical engine extraction are outside PR 1.

Earlier investigations are preserved in the [archive](archive/). They do not replace current source, accepted API decisions, or executed qualification evidence.
