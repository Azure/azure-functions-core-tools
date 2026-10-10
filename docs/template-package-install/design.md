## Context

See `proposal.md` for motivation and `specs/template-package-install/spec.md` for lifecycle behavior. The companion `../template-engine-integration/` change defines the shared TemplateEngine boundary, command-scoped `Templater` lifetime, func settings hive, and rule that raw TemplateEngine types remain internal.

The current code has the following relevant surfaces:

- `Templates/Engine/Templater.cs` creates the func host, engine settings, `TemplateCreator`, and `TemplatePackageManager`. It currently exposes those engine objects and only wraps template listing.
- `Commands/NewCommand.cs` is a singleton command for execution and listing. It has no lifecycle subcommands or lifecycle options.
- `Templates/NewCommandRunner.cs` owns the existing project-aware execution pipeline and should not absorb package-management behavior.
- `Templates/TemplatesServiceCollectionExtensions.cs` registers the current template orchestration services as singletons.
- `Hosting/BuiltInCommands.cs` registers `NewCommand` directly as a top-level command; nested commands need concrete registrations so they are not also added at the root.
- `Workloads/Catalog/IPackageSourceProvider.cs` already implements the required `--source`, `FUNC_CLI_WORKLOADS_SOURCE`, and nuget.org precedence.
- `Workloads/Install/WorkloadInstaller.cs` already reads NuGet package types with `NuGet.Packaging`, but its validation is specific to `FuncCliWorkload`.

Microsoft.TemplateEngine 10.0.301 provides a global `IManagedTemplatePackageProvider` through `TemplatePackageManager.GetBuiltInManagedProvider(InstallationScope.Global)`. The provider resolves an `InstallRequest` to exactly one installer, persists package registration in the func hive, and exposes install, update, uninstall, and latest-version operations. Its replacement path removes the old managed package before the installer acquires the replacement, so its native behavior alone does not satisfy the required failure-safe replacement guarantee.

## Goals / Non-Goals

**Goals:**

- Add package lifecycle behavior without creating a second TemplateEngine bootstrap path.
- Keep TemplateEngine package providers, installers, requests, and results behind `Templater`.
- Make all lifecycle subcommands use one typed orchestration path.
- Reuse existing feed configuration and NuGet package inspection infrastructure.
- Preserve the existing installation exactly when replacement fails.
- Keep lifecycle operations independent of project, stack, language, and bundle resolution.

**Non-Goals:**

- Change template listing, group filtering, argument parsing, or invocation responsibilities defined by `template-engine-integration`.
- Introduce a func-owned package registry parallel to TemplateEngine's managed package registry.
- Reimplement TemplateEngine package-expression parsing, NuGet version resolution, acquisition, or installer selection.
- Add batch update, package search, prerelease selection, or additional installer types.
- Treat folder sources as NuGet packages or require a `.nuspec` in a template folder.

## Decisions

### Package lifecycle extends the Templater boundary

The implementation first applies the `template-engine-integration` ownership model: `ITemplaterFactory` creates one disposable `Templater` for a command invocation, and the raw settings, creator, package manager, provider, and managed packages are not public.

This change adds func-owned lifecycle methods to that facade:

```csharp
Task<TemplatePackageInstallResult> InstallPackageAsync(
    TemplatePackageInstallRequest request,
    CancellationToken cancellationToken);

Task<TemplatePackageUpdateResult> UpdatePackageAsync(
    TemplatePackageUpdateRequest request,
    CancellationToken cancellationToken);

Task<TemplatePackageUninstallResult> UninstallPackageAsync(
    TemplatePackageUninstallRequest request,
    CancellationToken cancellationToken);
```

The methods create TemplateEngine `InstallRequest` and `UpdateRequest` objects, select the global managed provider, inspect managed package identity and source details, translate installer results, rebuild template cache when needed, and return only func-owned models.

Lifecycle commands create `TemplateEngineContext` with the resolved command directory and no project or bundle. This is an allowed context from `template-engine-integration`: package management needs the shared func hive and host components but does not need compatibility context.

**Alternative considered:** inject or expose `TemplatePackageManager` directly to commands. This leaks external types, permits callers to bypass func ownership and rollback policy, and conflicts with the companion integration design. It is rejected.

**Alternative considered:** add a separate `TemplatePackageService` that bootstraps its own host and settings. This risks a different hive, component set, or cache policy from listing and execution. It is rejected.

### Lifecycle operations are nested subcommands

Three concrete nested commands are added:

```text
NewInstallCommand    -> func new install <package>
NewUpdateCommand     -> func new update <package>
NewUninstallCommand  -> func new uninstall <package>
```

`BuiltInCommands` registers these concrete types as singletons. `NewCommand` receives them and adds them to `Subcommands`, following the existing `WorkloadCommand` registration pattern.

The nested commands own their applicable `--source` and `--force` options. Command validators reject `--source` where package acquisition is not performed, unsupported force combinations, and missing or whitespace package expressions.

Lifecycle subcommands delegate to `TemplatePackageCommandRunner`. They are recognized during stage A and bypass the template-specific reparsing described by `template-engine-integration`. `NewCommandArgPreparer` is not extended with lifecycle behavior and will eventually be replaced by that change's strict parsing design.

**Alternative considered:** add legacy option aliases on the parent command. The aliases overload template execution parsing, complicate diagnostics, and preserve a form that func does not need for compatibility. They are rejected in favor of subcommands only.

### The command layer owns orchestration and rendering

`TemplatePackageCommandRunner` is a stateless command-layer orchestrator registered through DI. It:

1. classifies the request as folder or NuGet without acquiring the package;
2. validates whether `--source` is legal;
3. resolves the NuGet feed only for NuGet operations;
4. creates one command-scoped `Templater`;
5. calls the matching lifecycle method;
6. passes the func-owned result to `TemplatePackageCommandRenderer`.

`TemplatePackageCommandRenderer` is the only lifecycle component that writes through `IInteractionService`. It renders installed, replaced, already-installed, updated, no-update, and uninstalled results, plus next-action guidance.

Expected user failures are represented by specific domain exceptions from the templater boundary, including unsupported requests, package not found, wrong command owner, ambiguous package type, source conflict, invalid source usage, and failed replacement. The command boundary catches only the documented exceptions from one runner call and wraps them in `GracefulException` with the original exception preserved.

### Existing package-source precedence is reused

The runner depends on the existing `IPackageSourceProvider`. For a NuGet operation it calls `GetSource(explicitSource)` and places the resulting absolute feed URL in `InstallRequest.Details` using `InstallerConstants.NuGetSourcesKey`.

This preserves:

```text
--source
  -> FUNC_CLI_WORKLOADS_SOURCE
  -> nuget.org
```

The source provider is never queried for an existing folder path. Supplying `--source` for a folder install or for a folder-managed update fails before TemplateEngine mutates the hive.

The package expression itself remains unchanged when passed to TemplateEngine. Installer `CanInstallAsync` remains authoritative for distinguishing supported folder, local NuGet, and NuGet package-ID forms. The command-level classifier exists only to enforce source-option legality and must not reinterpret package IDs or versions.

**Alternative considered:** add a template-specific environment variable and feed resolver. This would duplicate existing configuration and violate the required shared precedence. It is rejected.

### Func-owned projections preserve TemplateEngine identity

An internal `TemplatePackageDescriptor` projects each `IManagedTemplatePackage`:

```text
TemplatePackageDescriptor
|- Identifier
|- Version
|- InstallerId
|- InstallerName
|- SourceIdentity
|- IsFolder
|- IsLocalPackage
`- MountPoint
```

`Identifier`, installer identity, and installer-specific details come from TemplateEngine. For NuGet packages, `SourceIdentity` is the normalized source recorded by the NuGet managed package. For folders, source identity is the normalized full directory path. Source comparison is case-insensitive for HTTP scheme and host, preserves path/query semantics, and uses platform path comparison for folders.

Commands never compare display names or reconstruct source identity. The descriptors remain internal to `Templater`; public results expose only identity, version, source display value, and operation outcome.

### Package validation occurs before the live mutation

Every install or replacement first runs in an isolated temporary func hive using another `Templater` created by the same factory and the same `InstallRequest`. This preserves TemplateEngine package-expression parsing, installer selection, NuGet resolution, vulnerability checks, and acquisition behavior while keeping the live hive untouched.

The staged managed package is validated:

- A NuGet package is opened with `NuGet.Packaging.PackageArchiveReader`.
- Package types are classified as `FuncTemplate`, `FuncCliWorkload`, both, or neither.
- Only `FuncTemplate` alone is accepted by `func new`.
- A folder package is accepted only when the staged `TemplatePackageManager` discovers at least one valid template from that managed package.
- Every accepted package must expose at least one valid template after TemplateEngine scanning.
- Every `.template.config/template.json` in the staged package passes the raw constraint declaration validation defined by `template-engine-constraints`, including third-party and folder packages. Engine scanning alone cannot establish this because it may silently discard malformed declarations.
- NuGet packages declared for another template host, including `FuncTemplate` plus `Template`, contain no func constraint types. This applies to prebuilt third-party packages as well as first-party packaging, while shared packages without func constraints remain accepted.

Raw validation is context-independent and does not evaluate requirements against installed workloads. Well-formed unknown constraint types remain available for fail-closed eligibility evaluation after installation. Invalid declarations reject the staged package before live hive mutation, including forced replacements and updates.

Preflight retains the resolved package identity, version, installer/source identity, and content fingerprints of package-type metadata and every raw template configuration. The temporary engine session can be disposed after preflight, but this validation evidence remains available through the live transaction. Filesystem creation, copying, locking, and cleanup are behind injectable boundaries so tests do not depend on the process temp directory.

This staging may acquire a NuGet package twice: once for validation and once through the live managed provider. The actual acquired package must match the preflight identity and validation fingerprints and pass the same raw declaration and host-sharing checks before transaction commit. Compare the complete configuration inventory so added or removed templates cannot evade the check. A changed local archive or folder, or different content returned under the same package identity/version, fails the operation and restores the previous hive before the lifecycle lock is released. Invalid staging is rejected before live mutation; a later acquisition mismatch is a transaction failure, not permission to publish unvalidated content. Optimization can reuse an engine-supported acquisition artifact only if it preserves the original installer and source details.

**Alternative considered:** install into the live hive and uninstall an invalid package. That creates a visibility window for unsupported packages and can leave them registered if cleanup fails. It is rejected.

### Install follows an explicit state machine

After staging resolves the target package, `Templater.InstallPackageAsync` loads live managed packages and compares by TemplateEngine identifier, installer, and source identity:

```text
not installed
  -> install target

same source + same version
  -> return AlreadyInstalled without provider mutation

same source + different explicit version
  -> update to staged version, allowing upgrade or downgrade

different source + no force
  -> throw TemplatePackageSourceConflictException

different source + force
  -> transactionally replace with staged identity
```

The staged package's resolved version is the comparison value; func does not perform semantic version ordering for install. A different exact version from the same source is passed to TemplateEngine as the update target whether it is newer or older.

The `InstallRequest.Force` flag is not used as the func cross-source policy by itself. Func evaluates source identity first, because TemplateEngine force behavior is installer-specific and does not express the required ownership rule.

### Update and uninstall use managed package identity

`UpdatePackageAsync` resolves exactly one live managed package using TemplateEngine's installed identifier. It does not search the workload registry.

For NuGet packages, update source selection works as follows:

- no `--source`: use the source recorded on the installed managed package;
- explicit `--source`: resolve the latest package from that feed, whether or not it matches the recorded source.

Without an explicit source, the target version is obtained through TemplateEngine's latest-version check for the installed managed package. With an explicit source, the same staging flow used by install asks TemplateEngine to resolve the latest version from that feed. NuGet versions are compared with `NuGet.Versioning` only to enforce the update command's newer-only contract; TemplateEngine remains responsible for resolving the version and acquiring the package.

An installed package at or above the resolved version returns `NoUpdate` without provider mutation. A newer version from the same source is applied through `IManagedTemplatePackageProvider.UpdateAsync`. A newer version from a different explicit source uses the rollback-protected replacement path and records the requested source as authoritative. The explicit `update <package> --source <feed>` request authorizes that targeted source change; `--force` remains specific to cross-source install.

Folder packages report no update when TemplateEngine reports the folder as current. `--source` is rejected for them.

`UninstallPackageAsync` resolves the installed managed package and calls the provider's uninstall operation. It returns `NotInstalled` as a user error rather than silently succeeding. Workload registry and workload install directories are never consulted or modified.

### Replacement is protected by a func hive transaction

The pinned TemplateEngine global provider uninstalls the existing package before its installer acquires a replacement. `Templater` therefore wraps every operation that installs or replaces package content in `ITemplateHiveTransaction`, including first installs, so unvalidated acquisition results cannot become committed state.

The transaction:

1. acquires a func-owned cross-process lifecycle lock under the template settings directory;
2. snapshots TemplateEngine package registration, the affected package mount point, and template cache state;
3. invokes the managed provider operation;
4. validates actual acquired package metadata and raw configuration against the retained preflight evidence, then commits only after those checks, provider success, and cache rebuild;
5. disposes the engine session before rollback;
6. restores the snapshot byte-for-byte when acquisition, validation, registration, or cache rebuild fails;
7. recreates the command-scoped engine session against the restored hive before returning diagnostics.

All func template install, update, uninstall, listing, and execution entry points use the same lifecycle lock when opening or mutating package state. Read operations hold a shared lock; lifecycle mutations hold an exclusive lock. This prevents another func process from observing the transient provider state.

The proposed runtime handoff in `template-engine-integration` retains a live read lease while mounted catalog/selection/preview/invocation content remains in use. Acquisition callers dispose that session before lifecycle mutation and create a new reader afterward; no read-to-write lock upgrade is supported. Preflight uses an isolated hive and must not acquire a nested live read lease inside an exclusive lifecycle operation. Raw bytes, package metadata, configuration inventory, and validation fingerprints come from the store boundary, while semantic validation remains a shared consumer service. Lock implementation and engine recovery spike results remain lifecycle-owner responsibilities.

Both shared and exclusive lease waits honor command cancellation, including acquisition races, and expose a func-owned wait reason for rendering through `IInteractionService`. A canceled writer that has not begun mutation leaves the live hive and existing readers unchanged. Reader/writer ownership must release on process exit or be safely recovered after termination without relying on disposal, and slow live owners cannot be evicted merely because a wait expires or lock metadata looks old. Reclaiming an abandoned writer's lease does not validate its package state. Store-owned interrupted-transaction/cache recovery must complete, or fail closed with a diagnostic, before another session opens the live hive. This uses the same recovery owner and does not add a second lock protocol.

The transaction implementation depends on an injectable template-hive filesystem and lock abstraction. It does not deserialize or rewrite `packages.json`; restoring the exact snapshot avoids taking ownership of TemplateEngine's private persistence schema.

Uninstall is not rolled back after a successful provider result because deletion is the requested outcome. First install, update, same-source version replacement, and forced cross-source replacement are rollback-protected. A failed first install restores the previous hive without leaving the new package registered.

**Alternative considered:** trust the provider operation as atomic. Its current implementation deletes the previous NuGet package before downloading the replacement, so this does not satisfy the spec. It is rejected.

**Alternative considered:** restore by reinstalling the previous package from its source. The source may be unavailable and reinstalling can change installer details, so it cannot guarantee preservation. It is rejected.

### Package ownership classification is shared with workloads

The NuGet package-type logic in `WorkloadInstaller` is extracted into `IFuncPackageTypeClassifier`. It classifies package types without deciding which command is running.

Template preflight uses it to enforce `FuncTemplate`. Workload installation uses it to retain `FuncCliWorkload` validation and add reciprocal guidance:

- `FuncTemplate` only -> direct the user to `func new install`;
- `FuncCliWorkload` only -> direct the user to `func workload install`;
- both -> reject as ambiguous;
- neither -> reject as unsupported.

For a local `.nupkg`, classification happens directly. For a NuGet package ID that the workload catalog rejects because it filters to `FuncCliWorkload`, a narrow ownership probe checks that exact ID against the selected source before returning not found. The probe is used only for error guidance and does not install or register the package.

Folder template installs do not use NuGet package-type classification. Successful TemplateEngine discovery is their ownership proof.

### Cache and result handling stay inside Templater

After a successful install, update, replacement, or uninstall, `Templater` rebuilds the template cache before returning. Results are projected into func-owned records:

```text
TemplatePackageInstallResult
  Installed | Replaced | AlreadyInstalled

TemplatePackageUpdateResult
  Updated | NoUpdate

TemplatePackageUninstallResult
  Uninstalled
```

Each result includes normalized identifier, old and new versions when applicable, and source display values. TemplateEngine error codes are mapped to specific domain exceptions at the integration boundary. Unexpected exceptions remain unwrapped defects.

Cancellation is passed through staging, source resolution, provider operations, locking, snapshot I/O, cache rebuild, and rendering. Cancellation triggers rollback when live mutation has started and then propagates `OperationCanceledException`.

## Risks / Trade-offs

- **[Preflight can download a NuGet package twice]** -> Accept the initial reliability cost; measure it and optimize only through a TemplateEngine-supported artifact handoff that preserves source identity.
- **[Hive snapshots add disk and latency overhead]** -> Snapshot only registration, affected mounts, and cache files; perform it only for replacement-capable operations.
- **[TemplateEngine persistence layout may change]** -> Treat snapshots as opaque files and centralize path discovery behind the `Templater` adapter for the pinned package version.
- **[Two processes can race package reads and writes]** -> Use the shared/exclusive func lifecycle lock for every `Templater` operation against the hive.
- **[Source normalization can misclassify feeds]** -> Preserve TemplateEngine details and normalize only URI scheme/host and platform path comparison rules.
- **[Wrong-command probing adds a network request after not found]** -> Probe only exact unresolved package IDs and only to improve ownership guidance.
- **[The existing `NewCommand` is already complex and singleton-scoped]** -> Keep lifecycle logic in nested commands and a dedicated stateless runner; parent options only normalize into typed requests.

## Migration Plan

1. Land or implement the `template-engine-integration` factory, command-scoped facade, private engine objects, and shared hive locking foundation.
2. Extract shared package-type classification while preserving existing workload behavior.
3. Add func-owned package requests, results, source descriptors, staging, and hive transaction primitives.
4. Add `Templater` lifecycle methods and focused isolated-hive tests.
5. Add lifecycle runner, renderer, nested commands, validators, and DI registrations.
6. Add reciprocal wrong-command guidance to workload installation.
7. Remove direct test and product access to `Templater.PackageManager` after callers migrate to func-owned methods.

Rollback removes the new command registrations and option routing. Existing TemplateEngine package registry and package files remain compatible because this design continues to use the built-in global managed provider and does not introduce a parallel persistence format.
