## 1. TemplateEngine Boundary Foundation

- [ ] 1.1 Complete the command-scoped `ITemplaterFactory`, immutable context, private engine session, and func-owned model boundaries required from `template-engine-integration`.
- [ ] 1.2 Remove product access to `Templater.Settings`, `Templater.Creator`, and `Templater.PackageManager`, retaining only internal adapters and focused test seams.
- [ ] 1.3 Add func-owned install, update, and uninstall request/result records plus domain exceptions for unsupported requests, ownership, source conflicts, invalid source usage, and failed replacement.
- [ ] 1.4 Register the templater factory and package lifecycle infrastructure through `TemplatesServiceCollectionExtensions` with singleton factories and command-scoped facade instances.

## 2. Package Ownership and Source Resolution

- [ ] 2.1 Extract NuGet package-type inspection from `WorkloadInstaller` into `IFuncPackageTypeClassifier` with template-only, workload-only, ambiguous, and unsupported outcomes.
- [ ] 2.2 Update workload package validation to consume the shared classifier without changing successful `FuncCliWorkload` installation behavior.
- [ ] 2.3 Add template package-expression classification for existing folders, local NuGet packages, and installer-resolved NuGet IDs without reinterpreting TemplateEngine package syntax.
- [ ] 2.4 Reuse `IPackageSourceProvider` for `--source`, `FUNC_CLI_WORKLOADS_SOURCE`, and nuget.org precedence, and avoid resolving a feed for folder operations.
- [ ] 2.5 Add tests for NuGet ownership combinations, explicit and environment feed precedence, default feed selection, and rejection of `--source` for folders.

## 3. Isolated Package Preflight

- [ ] 3.1 Add an injectable temporary-hive provider and cleanup boundary suitable for deterministic tests.
- [ ] 3.2 Implement isolated `Templater` preflight using the same TemplateEngine install request, host components, and installer selection as the live operation.
- [ ] 3.3 Validate staged NuGet packages with `IFuncPackageTypeClassifier` and require `FuncTemplate` without `FuncCliWorkload`.
- [ ] 3.4 Validate staged folder packages and all accepted NuGet packages by requiring TemplateEngine to discover at least one template from the managed package.
- [ ] 3.5 Translate staged installer failures and vulnerabilities into func-owned domain outcomes while preserving cancellation.
- [ ] 3.6 Add preflight tests for valid NuGet and folder packages, workload-only packages, dual package types, missing package types, empty folders, malformed templates, acquisition failures, and cleanup.

## 4. Hive Transaction and Concurrency

- [ ] 4.1 Add injectable shared/exclusive template lifecycle locking under the func template settings directory.
- [ ] 4.2 Update all `Templater` read and lifecycle entry points from `template-engine-integration` to acquire the appropriate shared or exclusive lifecycle lock.
- [ ] 4.3 Implement opaque snapshots of TemplateEngine package registration, affected package mounts, and template cache state without parsing or rewriting the provider's persistence format.
- [ ] 4.4 Implement commit, disposal-before-rollback, byte-for-byte restore, and engine-session recreation for failed replacement operations.
- [ ] 4.5 Rebuild the TemplateEngine cache before transaction commit and trigger rollback when cache rebuilding fails or cancellation occurs after live mutation begins.
- [ ] 4.6 Add tests that inject failures during provider uninstall, acquisition, registration, cache rebuild, cancellation, and rollback, proving the previous package remains installed and usable.
- [ ] 4.7 Add concurrent reader/writer tests proving listing and execution cannot observe transient replacement state.

## 5. Templater Install Lifecycle

- [ ] 5.1 Project managed TemplateEngine packages into internal descriptors containing identifier, version, installer identity, source identity, folder/local flags, and mount point.
- [ ] 5.2 Implement installer-specific source normalization and equality using URI scheme/host rules for feeds and platform path rules for folders.
- [ ] 5.3 Implement `Templater.InstallPackageAsync` for a previously uninstalled package through the built-in global managed provider.
- [ ] 5.4 Return `AlreadyInstalled` without provider mutation for the same identifier, source, and resolved version.
- [ ] 5.5 Implement rollback-protected same-source replacement for a different explicitly resolved version, allowing both upgrades and downgrades.
- [ ] 5.6 Reject a different-source install without force and implement rollback-protected replacement when force is supplied.
- [ ] 5.7 Map TemplateEngine install results and error codes into func-owned results and specific domain exceptions, then rebuild the template cache.
- [ ] 5.8 Add isolated-hive tests for first install, same-version no-op, upgrade, downgrade, source conflict, forced source replacement, result projection, and cache refresh.

## 6. Templater Update and Uninstall Lifecycle

- [ ] 6.1 Resolve update and uninstall targets from TemplateEngine managed package identity without consulting the workload registry.
- [ ] 6.2 Implement update without `--source` using the installed package's source and TemplateEngine latest-version check.
- [ ] 6.3 Implement update with `--source` by staging TemplateEngine resolution from that feed and comparing the resolved NuGet version to the installed version.
- [ ] 6.4 Return `NoUpdate` without mutation when the installed version is current or newer, including TemplateEngine folder packages.
- [ ] 6.5 Apply same-source updates through the managed provider and different-source explicit updates through rollback-protected replacement.
- [ ] 6.6 Implement uninstall through the package's managed provider, return a targeted not-installed error, and rebuild the template cache.
- [ ] 6.7 Add tests for same-source update, explicit-source update, source-authority changes, no-update, folder update, source rejection for folders, uninstall, missing package, and workload-store isolation.

## 7. func new Command Surfaces

- [ ] 7.1 Add `NewInstallCommand`, `NewUpdateCommand`, and `NewUninstallCommand` with required package arguments and one-line help descriptions.
- [ ] 7.2 Register the nested commands as concrete singletons in `BuiltInCommands` and attach them to `NewCommand` without exposing them as top-level commands.
- [ ] 7.3 Add string-valued `--install`, `--update`, and `--uninstall` convenience options plus `--source` routing on the parent `NewCommand`.
- [ ] 7.4 Add validators for mutually exclusive lifecycle operations, incompatible execution/list inputs, invalid source combinations, unsupported force combinations, and missing package values.
- [ ] 7.5 Ensure lifecycle forms bypass template-specific stage-B argument parsing and do not extend `NewCommandArgPreparer`.
- [ ] 7.6 Add `TemplatePackageCommandRunner` that resolves operation kind and source, creates one lifecycle `Templater`, and calls the matching func-owned method.
- [ ] 7.7 Add `TemplatePackageCommandRenderer` using `IInteractionService` for installed, replaced, already-installed, updated, no-update, uninstalled, and next-action output.
- [ ] 7.8 Catch only documented lifecycle domain exceptions at the command boundary and wrap them in `GracefulException` with inner exceptions preserved.
- [ ] 7.9 Add command tests proving canonical and convenience forms have identical validation, output, exit codes, source behavior, cancellation, and lifecycle results.

## 8. Reciprocal Workload Guidance

- [ ] 8.1 Return `func new install` guidance when `WorkloadInstaller` inspects a local NuGet package declaring only `FuncTemplate`.
- [ ] 8.2 Add a narrow exact-ID ownership probe after workload catalog not-found results so feed-based template packages receive the same guidance.
- [ ] 8.3 Preserve ambiguous and unsupported package-type errors and ensure `--force` never bypasses command ownership.
- [ ] 8.4 Add workload command and installer tests for template-only, workload-only, dual-type, unsupported, unresolved, and forced wrong-command requests.

## 9. Documentation and Verification

- [ ] 9.1 Document canonical and convenience lifecycle commands, source precedence, package ownership, downgrade behavior, and forced cross-source replacement.
- [ ] 9.2 Update template authoring documentation to require `FuncTemplate` for NuGet packages and explain folder validation by template discovery.
- [ ] 9.3 Run targeted templater, package lifecycle, `func new`, and workload installation tests.
- [ ] 9.4 Restore, build, and test the complete solution in Release configuration with warnings treated as errors.
