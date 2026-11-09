# Implementing the Template Engine

**Status:** The accepted Stack PR 3 candidate connects the qualified .NET C# isolated `net10.0` project and HTTP-item journeys. This document distinguishes implemented and wired behavior from narrower runtime qualification and broader Stack coverage that remain. The [component model](Templates-EndToEnd-Object-Model-Review.md) remains the implementation authority.

Node.js and other Stacks remain gated; this candidate does not qualify their content, project interpretation, task behavior, or runtime journeys.

| Status | Scope |
| --- | --- |
| **IMPLEMENTED** | The .NET path integrates the existing Template Engine and workload readiness, then uses Generate Files Privately followed by CLI-owned guarded Write Files to Project. |
| **QUALIFIED** | Representative C# isolated `net10.0` project and HTTP-item generation with Functions Host invocation on Windows x64 local fixed NTFS/ReFS paths. |
| **DEFERRED** | Node.js and other Stacks, broader platform/content coverage, remote acquisition, and broader package lifecycle. |
| **KNOWN LIMITATION** | G03 remains unresolved outside the representative path; unknown compatibility fails closed, and provider enumeration can be incomplete, so `NotFound` does not prove catalog completeness. |

## Current candidate

`InitCommand` and `NewCommand` orchestrate the existing Microsoft.TemplateEngine session, private generation, approved .NET tasks, and guarded writing of files to the project. The session remains internal to `Templater`; the workload owns project interpretation and Stack-specific effects, while the .NET workload supplies pinned packages and its workload-readiness integration registers and verifies them through Installed Templates. The route is limited to the supported Windows x64 local fixed NTFS/ReFS boundary and the representative .NET content.

The [packaging and acquisition contract](packaging-and-acquisition.md) remains authoritative for workload supply, Installed Templates registration, and deferred package lifecycle.

The .NET workload declares its required project and item package identities, pinned versions, and content-root-relative archive paths. A successful workload install, reuse, or update loads the installed workload and, when it declares required template packages, configures it before registering and verifying the complete set in the CLI hive. General CLI host startup and ordinary `func init`/`func new` execution do not register packages. Package identity/version policy for other Stacks and production replacement/repair remains outside this increment.

Resolution reports unavailable registered mounts and malformed template configurations rather than treating those scans as complete. The underlying `TemplatePackageManager.GetTemplatePackagesAsync` contract can suppress a provider-enumeration exception, log it, and return packages from providers that succeeded. The current host uses a null logger, and the available log record is not a supported, typed completeness result. The candidate therefore cannot reliably distinguish every suppressed provider failure from genuine absence. An incomplete catalog can appear empty, and `NotFound` does not always prove that all providers were available.

A focused observation exercised this boundary with the existing CLI hive: after a template was installed and selected successfully, corrupting `packages.json` left the template files present while listing returned no entries and resolution returned `NotFound`. This observes the existing failure mode; it is not a passing correctness regression. The candidate must not parse localized log text, enumerate providers again, or maintain a second discovery mechanism to infer completeness. This limitation remains for the active .NET route.

## Operation-private generation

`ITemplaterFactory.CreateAsync` captures registered local NuGet archives before constructing the operation's Template Engine session. The same bounded private content is used for registration, discovery, explicit invalid/duplicate checks, exact resolution, parameter evaluation, and generation; no original source is reopened after capture.

Capture uses the existing ancestry-pinned `OpenReadNoFollow` operation and BCL `ZipArchive`, not another template parser. It checks unsafe names, file/directory and case aliases, links, entry counts, declared and actual expanded lengths, and aggregate limits before the operation session is created. Capture supports Windows x64 on the safe reader's supported local fixed NTFS/ReFS paths; folder delivery remains disabled.

Each operation allows at most four packages, 4 MiB compressed per archive, 512 entries per archive, depth 16, 1 MiB per asset, 64 KiB per configuration asset, and 32 MiB of expanded content in total. These are bounded implementation limits, not new production packaging or distribution decisions.

Captured archives and the built-in managed provider's installed copies are sealed in `PrivateTemplateFileSystem`. The installed package identities and versions must match the original registrations. Disposing `Templater` disposes the private state.

`Templater.RenderAsync` uses Microsoft.TemplateEngine's `TemplateCreator`, including authored defaults, choices, generated/computed values and conditional processing. Private output permits only bounded rewinding/truncation of existing bytes needed by the Template Engine's conditional processor; cumulative write, file and directory quotas remain enforced. It validates declared primary outputs and returns private files plus evaluated post-action identifiers, arguments and `continueOnError` flags. It does not execute post-actions or write project files.

The .NET workload evaluates project inputs through the CLI-provided pinning callback before item generation and returns immutable engine-neutral bindings with an operation-scoped evidence lease. `func new` owns and disposes that compatibility result after selection, private generation, task preflight, and guarded file writing, keeping the evaluated inputs pinned throughout. The workload interprets the supported restore and package-reference task metadata. `func init` writes generated project files before restore/configuration finalization; `func new` writes files only after compatibility and task preflight. Existing project/package mutations outside the already-satisfied representative reference remain unsupported.

The writer remains create-only and reports actual and partial file-writing effects; no atomic rollback is claimed. Project input pinning detects source changes across compatibility evaluation and file writing.

## Deferred work

- Node.js package registration/content and Node.js project/item journeys remain unimplemented and unqualified.
- Runtime qualification beyond the representative .NET content and currently qualified platform remains deferred.
- Remote acquisition, search/install/update/uninstall UX, production package update, repair, replacement, distribution-channel, and package distribution decisions remain deferred.
- Template listing and broader `func new` command grammar remain unavailable in this migration increment.
- Quickstarts are project templates in the approved architecture. The existing Quickstart route remains until its migration is implemented and qualified.
- The Template Engine remains in the CLI for this milestone. Physical extraction under issue #5330 is deferred until after Ignite.

### Existing initialization limitations

Legacy `func init` routes retain their existing safety limitations: they can create the destination before validation, perform force cleanup before all Stack-specific checks, remove `.git` pointer files, and report success after some required configuration writes fail.

The qualified template-backed .NET path does not use `--force` and preserves adoption and repair behavior.

Useful source entry points include `src/Func/Templates/Engine/`, `src/Func/Commands/InitCommand.cs`, `src/Func/Commands/NewCommand.cs`, `src/Abstractions/Projects/`, and `src/Workloads/Stacks/`. Current source and tests define the implemented compatibility behavior.

## Candidate boundary

The representative .NET journey is implemented and wired. Node.js and remaining Stack coverage, platform expansion, and production package distribution remain gated on accepted content, compatibility and runtime evidence.

## Stack behavior to preserve

The six existing initializers provide the compatibility baseline. The exact supported combinations and defaults must be checked against current source and tests before replacing an initializer.

| Stack | Current project languages and important options |
| --- | --- |
| .NET | C# and F#; `--target-framework` defaults to `net10.0` in the documented baseline. `net11.0` is a C# case. The qualified C# isolated `net10.0` route uses the CLI Template Engine hive; other routes retain the existing initializer. |
| Node.js | JavaScript and TypeScript; bundle options and `--skip-npm-install` (default false). |
| Python | Python (`py` alias); bundle options and the current Python project scaffold. |
| Go | Go (`golang` alias; existing worker identifier `native`); bundle options and `--skip-go-mod-tidy` (default false). |
| Java | Java; bundle options and Maven-based scaffolding. |
| PowerShell | PowerShell (`pwsh`, `ps` aliases); bundle options, `--managed-dependencies` (default false), and `--runtime-version` (documented default 7.4, with 7.4/7.6 options). |

Common bundle options include `--no-bundles` and `--bundles-channel`. Preserve Stack/language alias handling, contributed defaults, interactive language selection for multi-language Stacks, and single-language configuration omission. The archived [Stack inventory](archive/func-init-execution/stack-inventory.md) provides detailed historical evidence; current source determines implemented behavior.

Keep the distinction between the language Stack and the Azure Functions Runtime. Do not interpret a worker alias or a public `--runtime-version` option as a new architecture term.

## Remaining qualification gates

| Area | Current status |
| --- | --- |
| .NET local package registration | Wired from the versioned workload payload into the CLI hive |
| Operation-private source capture and generation | Implemented for registered local NuGet archives within the documented bounds |
| .NET C# isolated `net10.0` task and binding path | Wired for the representative project and HTTP-item templates |
| Guarded file writing | Create-only on the qualified Windows x64 local fixed NTFS/ReFS boundary |
| Node.js and remaining Stacks | Not implemented or qualified by this candidate |
| Production package distribution, replacement, and expanded platforms | Deferred pending owner decisions and qualification |

## Historical foundation validation

### PR 1 tests

PR 1 covers factory creation, catalog listing, exact identity selection, alias ambiguity, duplicate identities, ineligible candidates, selection ownership, invalid selection, and actionable diagnostics. At foundation commit `fe9a0d9861f02596c68ad64c567e3ec0b16bad15`, the focused filter ran against an isolated snapshot with .NET SDK 10.0.403: 108 passed, 0 failed, and 0 skipped. That run does not cover subsequent review changes. A separate foundation-only snapshot with the local review corrections passed 52 `TemplaterTests` and `TemplaterFactoryTests`: 0 failed and 0 skipped. It does not include the deferred renderer.

### Representative .NET end-to-end journey

The retained Stack PR 3 representative journey uses a real .NET project template and HTTP-function template:

1. Stage the versioned workload payload and let `func init` register its local project and item packages into an isolated CLI hive.
2. Run `func init` and verify the resulting project with the normal project resolver.
3. Run `func new` against that project, passing a required parameter.
4. Verify the generated function uses the supplied parameter.
5. Start the approved Functions Host and verify actual function discovery and invocation.

A `host.json` file, successful listing, or basic text-file fixture alone does not establish this proof. One representative Stack does not establish broad production coverage.

### Additional acceptance coverage for broader delivery

The following scenarios are not established by the representative .NET increment unless explicitly listed in its retained validation evidence:

- **Discovery and selection:** missing content, multiple metadata variants, ambiguous aliases, ineligible exact IDs, duplicate identities, unavailable constraints, and no sibling substitution.
- **Parameters:** candidate-specific options before final selection; conditional required/disabled/default values; explicit empty and repeated values; interactive and noninteractive errors.
- **Content integrity:** changed or missing configuration/assets between selection and invocation; rendering from the verified selected content; unsupported engine features fail before destination effects.
- **`func init`:** all existing Stack options, adoption/repair, safe occupied non-project directories, write-if-missing behavior, complete-project refusal, force confirmation, `.git` protection, and required configuration failures.
- **`func new`:** project resolution, existing command grammar, template listing versus invocation, item collisions, guarded modifications, and no unrelated initialization configuration writes. An existing empty directory with at least one installed workload factory receives actionable `func init` guidance; other unresolved results retain their existing handling and do not imply initialization is required. Resolution failures stop before discovery; incompatibility stops before generation. None of these failures may initialize a project or modify the destination.
- **File writing:** path/ancestor/case checks, guarded no-replace or replacement on supported operating systems, cancellation, cleanup, and truthful partial-effect reporting.
- **Packaging:** isolated test registration versus real workload registration; missing or incompatible installed content; retry and version coordination.

## Validation status and limitations

The retained Stack PR 3 validation evidence records 2,851 passed / 0 failed / 1 skipped Func tests, 96 passed / 0 failed / 0 skipped .NET workload tests, and 61 passed focused readiness/lifecycle tests. It also records `git diff --check` passing. The Func suite includes the packaged workload journey: `func workload install` registers and verifies both pinned packages before `func init`; package identity, version, and archive hashes remain unchanged through `func init`, `func new`, and Host startup; the journey then builds the project and invokes the HTTP function. The host loaded `HttpExample`, logged a successful invocation, and the test verified the successful response body. MSBuild reported 2 project inputs; completion with the known root project produced 3 total inputs (1 `.csproj`, 0 `.props`, 2 `.targets`, 0 other), within the 256-input limit. These are retained results from the accepted candidate, not rerun during base integration.

The isolated host journey disables the generated local-storage connection in its child process and uses file-based secrets, so it does not require Azurite. Validation used isolated CLI and SDK first-run state and did not install machine-wide tooling.

The validation was run on Windows x64 with local fixed NTFS/ReFS paths. It does not qualify other platforms, Node.js or remaining Stacks, broader template content, production package replacement/update/repair, or production package distribution. A prior SDK invocation installed an ASP.NET Core HTTPS development certificate; it was not removed, modified, or trusted. Subsequent CLI validation used isolated process state and first-run environment flags.

Keep current initializers and the existing Quickstart route until their replacements or migrations are implemented and qualified. Package install/update/uninstall/search, producer-pipeline expansion, a general task framework, and physical engine extraction are outside PR 1.

Earlier investigations are preserved in the [archive](archive/). They do not replace current source, accepted API decisions, or executed qualification evidence.
