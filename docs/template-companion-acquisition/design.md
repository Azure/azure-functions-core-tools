## Context

The init design requires project templates to be separate from stack assemblies and deliberately leaves their acquisition unresolved. Today, installing a supported stack enables curated quickstart selection without a separate content-package install. The companion design preserves that default availability while execution moves to installed templates.

This is a draft acquisition proposal. The mapping location, option spelling, and shared-package ownership need review before implementation. The migration decision owns when the old quickstart command can be removed.

## Goals / Non-Goals

**Goals:**

- Acquire required basic templates and optional curated quickstarts as part of an explicit stack installation.
- Keep a stable package identity per stack and companion role, so new samples arrive through bundle versions rather than new package discovery.
- Preserve user-selected versions, sources, opt-outs, and existing package state.
- Report the real state of partially completed acquisition without claiming cross-store atomicity.

**Non-Goals:**

- Download or install packages from `func init` or `func new` template execution.
- Automatically update workloads or templates, or define update notifications.
- Republish samples, infer trust from package names, or redefine the publisher-trust contract.
- Add a second TemplateEngine package registry or silently traverse arbitrary NuGet dependencies.

## Decisions

### Reviewed mappings identify companions

A Functions-owned acquisition catalog maps an approved logical stack identity to basic and quickstart companion package IDs and allowed version ranges. It is separate from sample onboarding YAML, public template search, and the installed TemplateEngine catalog. It names published bundles, not individual repository URLs or template short names.

The proposed catalog is shipped or versioned with reviewed setup/profile acquisition data rather than supplied by arbitrary stack assemblies. Its exact artifact location and trust delivery mechanism remain review questions. A package prefix or stack alias is not evidence that a package is an approved companion. The stack identity, source policy, version range, and package identities must be resolved and validated before installation.

Conceptually, Python acquisition selects these roles:

```text
python
|- basic        one reviewed project/item template bundle
`- quickstarts  one reviewed curated sample bundle
```

The bundle IDs remain stable across source repository renames and sample additions. Multiple Functions stacks in a sample are not guessed from its advertised language. Their package placement and acquisition mapping require an explicit reviewed entry, while template configuration actions and constraints retain every required stack.

**Alternative considered:** derive companion IDs from `python` or a NuGet prefix. This allows naming to become an implicit trust and installation policy. It is rejected.

### Explicit stack installation authorizes companion acquisition

Both setup and direct installation of an approved stack use one acquisition planner. The plan includes the stack package, its basic bundle, and its curated bundle unless the user opts out. Non-stack workload installation remains a workload-only operation. Low-level workload deployment does not become a general template installer.

The planner renders companion packages and resolved versions in the normal interactive or non-interactive acquisition output. A user's explicit stack-install request authorizes the reviewed default companions; no extra per-package prompt is required. Existing source/trust consent must still be honored. Installation does not run template creation or template post-actions.

The proposed option is `--no-quickstarts`, available on setup and approved stack installation. The exact spelling remains under review. It omits only curated acquisition. Basic project and item templates remain required. Opting out does not uninstall existing curated packages or make their installed templates invisible.

When a stack is already installed, acquisition is still able to repair missing companions through the explicit stack/setup request without unnecessarily redeploying the stack. This is a command-orchestration change, not a change to the workload installer's existing single-package ownership contract.

### Existing lifecycle owns versions, sources, and registration

The planner uses the normal source precedence and existing workload version selection. Each template operation goes through the command-scoped templater lifecycle, its isolated validation, and its hive transaction. It does not write managed package registrations itself.

The companion mapping bounds template version selection. An explicit workload version does not invent an identical template version; independently versioned companions must be compatible with the selected mapping. Preview handling follows the approved acquisition/profile policy, not an assumption that bare version ranges automatically include prereleases.

Already-installed packages are reused only when their version and source meet the plan. A direct template install or version pin is not silently replaced, downgraded, or switched to another source by stack installation. A conflict produces the installed and proposed identities and an explicit next action. Cross-source replacement requires the template lifecycle's explicit authorization. The meaning of pins and ownership receipts must be agreed with the profile and lifecycle owners rather than inferred from an installed version alone.

A successful acquisition receipt records only the stack-to-companion association and user choices in the existing acquisition state. TemplateEngine remains authoritative for whether a template package is installed and which source/version owns it. No separate user configuration file or duplicate template package registry is introduced.

### Acquisition is recoverable, not a cross-store transaction

Resolve the approved plan before writes, then install the stack and basic companions before the optional curated companion. Each package uses its owning subsystem's failure-safe installation boundary. If a later package fails, keep earlier successful packages and report their exact state. Return non-zero for incomplete requested acquisition and provide an idempotent retry path, including the option to omit curated acquisition.

Do not automatically uninstall a working stack or shared template package after a later failure. Cancellation stops further package operations while each active lifecycle operation performs its own cleanup or rollback. Offline acquisition succeeds only when every requested component can be supplied from verified local/installed state; it does not silently claim completeness or omit required packages.

Removing a stack does not automatically remove template packages. Packages may have been explicitly installed or shared. Automatic garbage collection and mixed-stack reference counting remain outside the initial proposal; explicit template uninstall stays authoritative.

### Upgrade coverage is part of migration qualification

Fresh installation and explicit acquisition on an upgraded machine must both supply the approved bundles. Existing template workloads are not treated as equivalent to a registered `FuncTemplate` package solely because their names match. Migration identifies which legacy ownership/content can be replaced or retained and preserves user-installed packages outside that mapping.

Opting out or removing a companion explicitly must not be reversed by an unrelated CLI invocation. A later explicit request to acquire or repair that companion may restore it. The command migration cannot rely on every existing user reinstalling their stack manually; the upgrade path and its consent must be documented and tested.

## Risks / Trade-offs

- **[Default acquisition adds download size]** -> Separate curated bundles, show the plan, and retain the quickstart-only opt-out.
- **[A shared bundle has several owners]** -> Avoid implicit deletion and agree association/pin behavior before adding cleanup.
- **[Two stores can be partially updated]** -> Keep per-package safety, report completed and missing components, and make retries idempotent.
- **[Installed bundles do not track new manifest entries]** -> Deliver additions through new bundle versions; define checks and automatic updates in separate work.

## Review Questions

1. Should the companion catalog extend reviewed profile/setup data or use a separately published Functions-owned artifact, and what authenticates it?
2. Is `--no-quickstarts` the right option name and scope for setup and direct stack installation?
3. How should acquisition receipts distinguish user pins and shared packages without duplicating TemplateEngine ownership?
4. Which explicit upgrade/repair flow supplies companions to existing machines without resetting a prior opt-out?
5. How should mixed-stack curated packages be placed without duplicate template identities in several bundles?

## Migration Plan

1. Agree catalog delivery, source/trust policy, flag spelling, and ownership with the acquisition and lifecycle owners.
2. Publish basic and curated bundles with complete supported-sample coverage.
3. Add the shared planner and invoke it from setup and approved direct stack installation.
4. Test fresh, already-installed, upgraded, pinned, opted-out, offline, cancelled, and partially completed acquisition.
5. Qualify the quickstart command replacement before removing its existing default-availability path.

Rollback restores the previous orchestration. Installed template packages remain managed by the template lifecycle and are not deleted as part of rollback.