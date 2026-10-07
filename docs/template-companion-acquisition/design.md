## Context

The init design moves basic project scaffolding out of stack assemblies into template packages. This proposal acquires those basic project/item companions with explicit stack installation. Today curated quickstarts use cached catalog metadata and download content only after selection; preserving that flow belongs to discovery and guided use, not stack companion preinstallation.

This is a draft basic-companion acquisition proposal. Mapping delivery, source/version policy, and shared-package ownership need review before implementation. The migration decision owns when the old quickstart command can be removed.

## Goals / Non-Goals

**Goals:**

- Acquire required basic project/item templates as part of explicit stack installation.
- Resolve reviewed companion package identities without prescribing curated package grouping.
- Preserve user-selected versions, sources, and existing package state.
- Report the real state of partially completed acquisition without claiming cross-store atomicity.

**Non-Goals:**

- Download or install packages from `func init` or `func new` template execution.
- Preinstall curated quickstarts or add a quickstart preinstallation/opt-out option to stack installation.
- Define the curated discovery manifest or its guided acquire-and-create action.
- Automatically update workloads or templates, or define update notifications.
- Republish samples, infer trust from package names, or redefine the publisher-trust contract.
- Add a second TemplateEngine package registry or silently traverse arbitrary NuGet dependencies.

## Decisions

### Reviewed mappings identify companions

A Functions-owned acquisition catalog maps an approved logical stack identity to basic project/item package IDs and allowed version ranges. It is separate from sample onboarding YAML, public discovery metadata, and the installed TemplateEngine catalog. It names approved basic companions, not curated repositories or template short names.

The proposed catalog is shipped or versioned with reviewed setup/profile acquisition data rather than supplied by arbitrary stack assemblies. Its exact artifact location and trust delivery mechanism remain review questions. A package prefix or stack alias is not evidence that a package is an approved companion. The stack identity, source policy, version range, and package identities must be resolved and validated before installation.

Conceptually, Python stack acquisition selects:

```text
python
`- basic project/item companion package references
```

A basic companion may contain multiple project and item templates; its package shape is not inferred from an individual template name. Curated samples instead have discovery references identifying their approved package, version, source, and template identity. A sample's required Functions stacks come from validated project declarations, not this basic companion map or one advertised language.

**Alternative considered:** derive companion IDs from `python` or a NuGet prefix. This allows naming to become an implicit trust and installation policy. It is rejected.

### Explicit stack installation authorizes companion acquisition

Both setup and direct installation of an approved stack use one acquisition planner. The plan includes the stack package and its basic companions only. Non-stack workload installation remains workload-only. Low-level deployment does not become a general template installer, and stack acquisition does not fetch curated quickstart payloads.

The planner renders companion packages and resolved versions in the normal interactive or non-interactive acquisition output. A user's explicit stack-install request authorizes the reviewed default companions; no extra per-package prompt is required. Existing source/trust consent must still be honored. Installation does not run template creation or template post-actions.

There is no quickstart preinstallation flag. Basic project and item companions remain required by approved stack setup. Existing curated packages are not uninstalled or hidden when this policy replaces the earlier preinstallation proposal.

When a stack is already installed, acquisition is still able to repair missing companions through the explicit stack/setup request without unnecessarily redeploying the stack. This is a command-orchestration change, not a change to the workload installer's existing single-package ownership contract.

### Existing lifecycle owns versions, sources, and registration

The planner uses the normal source precedence and existing workload version selection. Each template operation goes through the command-scoped templater lifecycle, its isolated validation, and its hive transaction. It does not write managed package registrations itself.

The companion mapping bounds template version selection. An explicit workload version does not invent an identical template version; independently versioned companions must be compatible with the selected mapping. Preview handling follows the approved acquisition/profile policy, not an assumption that bare version ranges automatically include prereleases.

Already-installed packages are reused only when their version and source meet the plan. A direct template install or version pin is not silently replaced, downgraded, or switched to another source by stack installation. A conflict produces the installed and proposed identities and an explicit next action. Cross-source replacement requires the template lifecycle's explicit authorization. The meaning of pins and ownership receipts must be agreed with the profile and lifecycle owners rather than inferred from an installed version alone.

A successful acquisition receipt records only the stack-to-companion association and user choices in the existing acquisition state. TemplateEngine remains authoritative for whether a template package is installed and which source/version owns it. No separate user configuration file or duplicate template package registry is introduced.

### Acquisition is recoverable, not a cross-store transaction

Resolve the approved plan before writes, then install the stack and required basic companions. Each package uses its owning subsystem's failure-safe boundary. If a later required package fails, keep earlier successful packages, report their exact state, return non-zero, and provide an idempotent retry path. There is no success path that silently omits required basic templates.

Do not automatically uninstall a working stack or shared template package after a later failure. Cancellation stops further package operations while each active lifecycle operation performs its own cleanup or rollback. Offline acquisition succeeds only when every requested component can be supplied from verified local/installed state; it does not silently claim completeness or omit required packages.

Removing a stack does not automatically remove template packages. Packages may have been explicitly installed or shared. Automatic garbage collection and mixed-stack reference counting remain outside the initial proposal; explicit template uninstall stays authoritative.

### Upgrade coverage is part of migration qualification

Fresh installation and explicit acquisition on an upgraded machine must both supply the approved basic packages. Existing template workloads are not equivalent to a registered `FuncTemplate` package solely because their names match. Migration validates the legacy ownership/content transition and preserves user-installed packages outside that mapping.

Removing a companion explicitly must not be reversed by an unrelated CLI invocation. A later explicit acquisition/repair request may restore it. The command migration cannot rely on every existing user reinstalling their stack manually; the upgrade path and its consent must be documented and tested.

### Guided curated use is a separate acquisition boundary

The discovery component builds and publishes a package/template index from configured feeds and approved curation records. Its CLI cache exposes sanctioned templates without requiring payload installation. Browsing and search are read-only. Sample onboarding YAML and the installed TemplateEngine catalog do not substitute for that index.

An explicit guided use action can confirm acquisition of a sanctioned template and call the lifecycle before shared init execution. It must show and validate the actual package, version, source, and curation approval, preserve pins/source choices, and require explicit non-interactive authorization where applicable. The discovery contract must finish lifecycle acquisition and verify the owning package, artifact/provenance, and full template identity in a fresh init session, not pass a name to unrestricted global resolution. Failure or cancellation before creation leaves the target unchanged. Logging is not consent. Guided use must not silently install or upgrade workloads.

That orchestration, its command placement, trust proof, and cache/offline policy belong to the discovery design and are dependencies of the quickstart migration. They do not add curated entries to the basic stack mapping or require all quickstarts to be distributed in one package.

## Risks / Trade-offs

- **[Basic companion acquisition adds a second store]** -> Show resolved references and retain per-package lifecycle safety.
- **[A shared package has several owners]** -> Avoid implicit deletion and agree association/pin behavior before cleanup.
- **[Two stores can be partially updated]** -> Keep per-package safety, report completed and missing components, and make retries idempotent.
- **[Discovery metadata and installed payloads differ]** -> Keep curated browsing separate from installed execution and explicit acquisition; do not promise automatic updates.

## Review Questions

1. Should the companion catalog extend reviewed profile/setup data or use a separately published Functions-owned artifact, and what authenticates it?
2. How should acquisition receipts distinguish user pins and shared packages without duplicating TemplateEngine ownership?
3. Which explicit upgrade/repair flow supplies basic companions to existing machines while preserving prior package choices?
4. How should approved installed-version reuse and independently versioned basic companions interact with profile/version selection?

## Migration Plan

1. Agree basic catalog delivery, source/trust policy, and ownership with the acquisition and lifecycle owners.
2. Publish qualified basic project/item companions for supported stacks.
3. Add the shared planner and invoke it from setup and approved direct stack installation.
4. Test fresh, already-installed, upgraded, pinned, offline, cancelled, and partially completed acquisition, including no curated preinstallation.
5. Qualify the separate discovery/guided-use dependency before removing the old quickstart command.

Rollback restores the previous orchestration. Installed template packages remain managed by the template lifecycle and are not deleted as part of rollback.