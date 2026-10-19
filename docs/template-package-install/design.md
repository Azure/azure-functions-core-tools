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

## Draft protected-store foundation

The initial protected-store layer implements the engine-internal ownership boundary, not the lifecycle commands or their acceptance policy. `ITemplateStore.OpenReadAsync` acquires shared ownership before native initialization. `ITemplaterFactory.CreateAsync` and `CreateProjectAsync` return a disposable runtime facade with that ownership attached. `OpenWriteAsync` returns a single-transaction exclusive scope. These internal APIs and the following mechanics remain subject to Francisco's store review and Sarah's runtime feedback.

The live store remains `<CLI home>/templates`. Lock admission and recovery files live in the sibling `<CLI home>/.template-store` directory, outside the snapshotted hive. File handles protect cooperating processes; they do not prevent an editor, older CLI, or other nonparticipant from changing files. Admission probes exclusive ownership on the control filesystem and fails visibly if the backend ignores that protection. Writer intents precede admission-gate acquisition so reader churn cannot prevent ticket registration; FIFO registered tickets prevent later readers from overtaking an admitted writer. OS-owned delete-on-close markers avoid explicit deletion while a handle remains open. A leftover marker is removed only after acquiring its exclusive OS ownership, never through age or a PID heuristic. Waiting and admission honor cancellation and expose `ActiveReaders` or `LifecycleOperation` through a callback; the caller owns terminal presentation. A writer cannot upgrade a live read flow: dispose that flow and discard its capabilities before requesting mutation.

Intent acquisition and stale inspection share `OpenOrCreate`, exact `FileShare.None`, and `DeleteOnClose`. On Unix, opening can expose a transient filename before acquiring `flock`; that name alone is not an authoritative published owner. The verified .NET runtime 10.0.12 and 10.0.13 implementations compare the locked descriptor's device/inode with the current pathname before arming deletion, reopening a missing or replaced path. Both creation and inspection need this check: an inspector retaining an old descriptor must not delete a recreated live intent. Known contention is retried with cancellation; application-visible open/stat permission and I/O failures escape. Native Unix `FileStream` ignores some non-contention `flock` errors and unlink failures during handle release; the gate probe is not proof of every path's locking or cleanup permissions. Those backend and cleanup behaviors require separate Unix qualification. Registered ticket creation stays serialized under the gate. Windows held-marker inspection, unlink/rename refusal, cancellation and genuine permission controls are observed; controlled Unix create-before-flock interleaving remains a separate execution gate rather than a claim from source or stress counts.

Native initialization creates directories and providers, and native `GetTemplatesAsync` can rebuild and write caches even when the caller only lists. Shared runtime sessions therefore copy the opaque hive into private per-flow settings, including cache input, and retain the shared live-store lease for absolute package mount paths until engine disposal. Cache and scratch writes stay private. Persistent lifecycle cache rebuilding still targets the authoritative live hive. This deliberately favors safe reader overlap over live cache warming and duplicates package bytes; private-cache policy and whole-hive copy cost are review gates, not a completed optimization.

The exclusive scope snapshots the entire hive, without interpreting `packages.json`, before opening a mutating native session. A completed snapshot is armed by `recovery.ready`; incomplete snapshots without that journal have not authorized mutation. The mutation callback must translate unsuccessful provider results into failure. A separate required callback validates actual acquired content against retained preflight evidence. The store does not decide what packages, sources, declarations, or fingerprints are acceptable. Both callbacks receive the operation token. After they succeed, the scope rebuilds the live cache, checks pinned-engine cache diagnostics and persistence, disposes the engine, and removes the journal as its commit point. Cache or scan failures that the pinned package manager logs instead of throwing are made visible.

Failure invalidates the original engine before restoring prior files. Once restoration starts it uses an uncanceled token so cancellation cannot publish a half-restored hive. A fresh private engine environment is then constructed against the restored state; old catalogs and capabilities are not transferred. The caller inspects `TemplateStoreWriteSession.State` (`Committed`, `Restored`, or `RecoveryRequired`) and `RecoverySessionRecreated` after failure and starts any later runtime flow through the factory. `Restored` with `RecoverySessionRecreated == false` distinguishes successful disk restoration from failed environment recreation; that recreation failure still escapes. A scope is single-use and refuses disposal while its transaction or recovery is running. Restore failure leaves the journal and backup for repair/retry. New readers release their read lease, acquire exclusive recovery ownership, restore an armed interrupted transaction, and only then create an engine. A corrupt or incomplete armed snapshot fails closed. Backup cleanup is deferred to the next writer; runtime copies use existing best-effort temporary-directory cleanup.

If mutation, validation or cache rebuilding fails and cleanup or recovery also fails, the transaction reports an `AggregateException` containing the original failures in occurrence order. A lone failure, including cancellation after successful recovery, retains its original exception and stack. Failed disk restoration keeps the journal for the next owner to retry. Failed environment recreation after successful restoration remains distinguishable through `Restored` and `RecoverySessionRecreated == false`.

Execution claim, completion and disposal use one synchronized lifetime state machine. An overlapping execution is rejected, disposal cannot release the exclusive lease during mutation or recovery, and the disposed transition prevents a later execution from starting. The lifetime gate is not held across native work, callbacks or awaits. Cross-process cancellation tests use a parent signal after the child's waiting phase, not a timer that can expire before readiness.

Qualification remains pinned to Microsoft.TemplateEngine **10.0.301** (upstream tag commit `a397da1b5747f31a7d91d024609c6e529679b694`). Its global provider removes an incumbent before replacement acquisition; its package manager logs and swallows cache-write failures. Private Windows hives exercise those actual paths, successful replacement and fresh invocation, cancellation after removal, byte-hash restoration of registration/archive/cache, first-install failure, repeated operations, and stale capability rejection. Owned separate Windows processes exercise overlapping readers, reader churn behind a queued writer, canceled ticket removal, reader termination, and recovery before listing after writer termination. Linux/macOS execution, network/feed failures, hostile filesystem mutation, power-loss durability, and general third-party provider behavior are not established by that evidence.

This layer does **not** implement staging/trust/source/ownership policy, raw-content inventory or fingerprint production, `func new install/update/uninstall`, automatic installation, startup availability capture, parser policy, or public `new`/`init` cutovers. Future lifecycle orchestration must supply the real preflight-to-acquisition validation callback and remain inside this boundary. `ExecuteAsync` is a rollback-capable mutation foundation; it does not establish the separate uninstall-after-provider-success commit semantics described above. Factory integration is usable by the runtime facade but is not proof that every legacy template provider has migrated. Owner approval of these provisional contracts and platform qualification are landing gates.

Public Linux build 309467 executed the initial `5fda1aaa` candidate's native/store/process tests: 41 store cases passed and two Windows-specific namespace/access cases were explicitly skipped, including all six cross-process cases passing. Their bodies have no Linux/backend early-return guard. This qualifies those operations on that worker's temporary filesystem, not all filesystems or a controlled Unix open-before-flock interleaving. The later synchronized-lifetime and parent-signaled-cancellation changes require their own exact-head qualification; the initial run is not reused as execution proof for those changes. macOS, controlled Unix inode interleavings and backend/cleanup permission failures remain separate gates.

## Staged basic companion acquisition

The internal, unregistered `Templates/Acquisition/BasicAcquisitionRunner` stages
explicit setup/repair under the user-selected **CLI-bundled mapping with
retained-stack reconstruction** option. It does not change public setup, init,
new, quickstart, or profile behavior. Ahmed owns acquisition (#5649), Sarah owns
first-party inventory/content (#5647), and Francisco owns publishing and the
template lifecycle/store (#5629, #5631). The thin basic-only lifecycle adapter is
provisional and requires those owners' review before landing or public activation.

`BasicCompanions.json` is an embedded CLI-release resource, independent of
overridable project/user/remote profiles. Its production inventory is deliberately
empty and yields visible `Unavailable`/`MissingMapping` guidance, not successful
acquisition. No example package, feed or version is shipped as production approval.
The test assembly separately embeds a synthetic cohort and generates owned
archives, computes their digests before invoking the reader, and supplies explicit
fixture ownership evidence. That qualifies a hypothetical producer/consumer
contract, not published runnable inventory.

Schema version 1 requires a revision, explicit logical stack package IDs,
nonoverlapping applicable stack-version ranges, approved effective feed URLs,
and basic companion IDs/compatible ranges. Each exact approved artifact records
package ID/version/source, SHA-256, content revision, explicit basic-only
qualification, and a complete raw configuration path/identity/type inventory.
The manifest revision also identifies its feed-policy snapshot. A compatible
range does not authorize future unreviewed versions. The publisher must qualify
the *whole artifact* as basic-only: item/project tags and a package prefix cannot
establish that content policy. Exact-byte checks and the complete inventory enforce
the reviewed evidence; they do not decide whether a reviewed sample is basic.
Missing revisions, unsupported schemas, duplicate properties/records, overlapping
stack mappings and invalid metadata fail explicitly.

Before deployment, planning includes **every** covered persisted workload row,
including unselected and side-by-side rows. A logical pointer's ID/version is used
when present; otherwise only explicitly mapped literal IDs are recognized. Aliases
are never acquisition authority. All retained shared requirements are intersected
before writes, even if their companion never installed. An approved stack version
without historical coverage blocks acquisition with a request for corrected
release-bound data. Producers must retain historical mapping records and their
requirements unchanged across CLI releases; this draft cannot compare artifacts
from releases it has never received.

The user selected **"Repair only the requested stack; retain all compatibility
constraints (Recommended)"**. This is targeted repair, not machine-wide repair.
All retained requirements still constrain shared ranges before any deployment,
but only the requested stack's companions are acquisition targets. If retained A
needs missing T and requested B needs independent U, B acquires U without
installing/restoring T. `Completed` describes B's requested operation, not the
completeness of every installed stack. Repeating B remains idempotent, while a
later explicit A repair may restore T. The fixture journey keeps A's actual
persisted row through those operations and checks native U-only visibility before
explicit A repair. Existing shared-T intersection/conflict controls remain
unchanged. This decision clarifies the result scope without broadening side effects
or treating a reviewer-requested union as acquisition authorization.

The runner resolves the existing `IPackageSourceProvider` once and passes its
effective explicit/environment/default source to stack installation and companion
selection. Each contributing mapping must approve it; disallowed overrides fail
before stack deployment without alternate-feed fallback. A local installed stack's
source path is not a companion feed. This repair path uses the ordinary approved
HTTP(S) feed and does not define direct-local-stack acquisition policy.

Installed companion reuse requires actual native managed-package identity/version,
archive validation, and lifecycle-owned source/pin evidence keyed to that exact
artifact. The default unavailable ownership implementation fails closed; installed
versions do not imply ownership or consent. A compatible qualified older artifact
can be reused. No incumbent is automatically replaced, upgraded, downgraded, or
switched to another source; conflicts require a separate explicit owning-lifecycle
action. No duplicate template registry or success-only association receipt is added.

For a missing companion, the provisional adapter acquires only the selected exact
archive, validates its bytes, NuGet ownership, complete raw configurations and
constraints, and installs/scans it in a private preflight hive. It then composes
`ITemplateStore.OpenWriteAsync` / `TemplateStoreWriteSession.ExecuteAsync` with
native installation and **actual acquired-content validation**, followed by the
store's cache/commit boundary. Validation compares the live archive and native
discovered inventory again; a changed archive or incomplete engine discovery
fails rather than committing provider success. There are no no-op validation
callbacks, changes to store locking, or template invocation/post-actions.
Acquiring through a staged local archive is not itself durable feed/pin provenance:
the lifecycle owner must supply that input before production reuse is enabled.

Typed outcomes distinguish completed, not applicable, unavailable, rejected,
check-incomplete, partial failure, and cancellation, retaining completed identities
and the actual cause/source diagnostics. Source argument handling is scoped to the
source-provider call. Malformed nuspec XML/version data is classified as an invalid
artifact at inspection, not a source conflict. Known stack feed HTTP/protocol
failures and transport timeouts are classified around installation; caller
cancellation stays distinct and unrelated argument errors escape. A successful
stack row remains after companion failure, so a
new process reconstructs its requirements before considering another stack. A
removed stack contributes none; reinstalling its mapped version restores the
requirement. Cancellation stops further operations and the protected store owns
active template recovery. Earlier successful packages are not uninstalled.
Check mode, native browsing and non-stack requests acquire nothing. Offline repair
reuses verified installed state; the default payload source reports a missing
payload rather than querying feeds. A separately injected verified local provider
can supply exact offline bytes without changing approval.

Public command integration, publisher-qualified production rows, historical upgrade
coverage, real lifecycle source/pin provenance, concurrent workload-acquisition
serialization, complete lifecycle replacement/update/uninstall, and cross-platform
qualification remain separate gates. The runner observes workload state but does
not add a cross-store transaction or serialize another process changing workloads
after planning. Curated registration/proof/grammar remains #5640/#5653/#5651; no
curated preinstallation, general scanner or second sample registry is introduced.
This increment is not M5 or production acquisition completion.

### Unresolved shared workload mutation protocol

The single-writer fixture journey does not qualify concurrent workload state.
`BasicAcquisitionRunner` reads an installed snapshot before planning;
`WorkloadDeploymentService.InstallAsync` can later replace the physical directory
and call `SaveWorkloadAsync` or move logical ownership. `WorkloadStore` reads,
modifies and atomically renames the registry without a lease spanning that
read-modify-write. Two writers can therefore publish stale aggregate state or add
a disjoint retained requirement after acquisition has planned. Atomic file
replacement prevents partial serialization, not lost updates or stale planning.
The new composition inherits and exposes that risk. The template-hive lease does
not protect workload directories or the workload registry.

Before production activation or landing as a concurrent-safe acquisition feature,
the workload/lifecycle owners must agree a cohesive protocol covering every local
and catalog install, update, uninstall, explicit/logical ownership attach/remove/
move, and direct registry save/remove/replace, including setup and this runner.
No acquisition-private lock can establish safety while other mutators ignore it.
Francisco's lifecycle/store review is required, coordinated with workload owners.
The next protocol proposal must answer:

1. Which authoritative workload lease or snapshot revision spans validation and
   deployment, and how do every mutator and retained-row reader participate?
2. What is the global order between workload ownership and template read/write
   admission? Can slow payload acquisition happen before admission with a
   mandatory recheck, without deadlock or accepting a changed requirement?
3. What completed-state and recovery evidence survives cancellation/process death
   after stack persistence or during companion commit, and which owner repairs it
   without silently removing a working stack or publishing a stale registry?

These are unresolved landing/activation questions, not a locking implementation
or a claim that `Completed` guarantees concurrent machine-wide consistency.
