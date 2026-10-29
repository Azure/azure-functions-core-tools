# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

## Contract boundaries

[Project compatibility checks for function templates](../template-engine-constraints/design.md)
are a separate read-only boundary. Their public stack-reporting contracts and
internal compatibility results do not expose Microsoft.TemplateEngine types.

The proposal, design and task list in this directory preserve the earlier
engine-integration plan. They are not evidence of completed resolution,
preparation or command integration. See the
[Templates overview](../templating-system/design.md) for contract navigation
and historical scope, and
[PR #5767](https://github.com/Azure/azure-functions-core-tools/pull/5767)
for Foundation implementation and validation evidence.

## Stack enablement contract

This contract defines the minimum stack-owned work for package-backed project
and HTTP-item generation. Merged Foundation remains authoritative for
[project compatibility](../template-engine-constraints/design.md).
The responsibilities below are implementation requirements, not evidence that
the shared path is complete.

This guide does not settle package layout, metadata placement, producer build
hooks, or private consumer APIs. Reconcile those details with the shared
integration contract. Assignments, delivery status, and pending decisions belong
in the [dated readiness assessment and work item](../func-init-execution/stack-inventory.md#pr-3-stack-readiness).

### Ownership

| Owner | Supplies |
|---|---|
| Stack/content owner | Project content, HTTP item content, native metadata, capability meanings, project inspection and reports, agreement between requirements and reports, producer/content validation, and stack-specific qualification. |
| Shared Templates infrastructure | Package intake, template selection and parameter parsing, declaration validation and translation, project compatibility matching, native template execution, prepared operations, and safe publication. |

Capability IDs are opaque to shared infrastructure. The stack owner documents
their meanings alongside the stack's content and inspection tests, following the
[Foundation contract](../template-engine-constraints/design.md#capability-definitions-and-ids).
Adding a stack does not require central capability registration or new namespace
governance. Stack, language, workload, package, and Runtime labels do not establish
capability support.

### Content and inspection supplied by a stack

1. Supply one native project template and one matching HTTP item template.
   Use Microsoft.TemplateEngine content with `.template.config/template.json`,
   stable identities and short names, and the appropriate `tags.type` value
   (`project` or `item`). Start from identified producer content, not a new
   stack-specific CLI engine.
2. Keep supported parameters, types, choices, required values, and defaults in
   native template symbols. Use `.template.config/func.host.json` when aliases
   or visibility need customization. Preserve canonical symbol names. The
   [metadata contract](design.md#host-specific-metadata-is-loaded-for-the-func-host)
   describes this projection. Enabling another language stack does not require
   new command-owned CLI options. Template-specific arguments come from the
   template's native metadata. Preserve authored defaults and whether each value
   was supplied explicitly. This guide defines no new HTTP parameters.
3. Declare the HTTP item's required capabilities using Foundation's
   [publisher declaration](../template-engine-constraints/design.md#publisher-declaration):
   `type` is `func-project-capabilities` and `args` is a nonempty conjunction of
   capability IDs. Every required ID must have the same meaning in content and
   inspection. This neutral declaration is not an implemented engine constraint
   or a settled package location. Shared integration must preserve and validate
   the exact raw declaration before engine normalization can discard information.
4. Inspect the inputs needed to establish those capabilities under
   [B1](../template-engine-constraints/design.md#templates-only-inspection-b1).
   Capture immutable reports on the existing
   [FunctionsProject](../../src/Abstractions/Projects/FunctionsProject.cs).
   Use Supported, Unsupported, or no report under the existing contract.
   Do not infer support from ordinary project classification.
5. Produce package content from identified, version-pinned sources.
   Include both native templates and their metadata. Record source provenance,
   an output inventory, and the artifact hash used for qualification. Validate
   the content and raw declarations before handing it to shared intake.
   Document the stack's repeatable production steps; reconcile the final package
   build hook and layout with the shared integration contract.

Existing template-workload catalogs and V2 jobs are useful producer evidence.
They are not automatically native package content for the new path. A stack
contribution must not add another selection parser, compatibility matcher,
publication mechanism, or legacy-provider fallback.

### How the shared CLI consumes content

Shared intake validates the supplied package content and declarations. Selection
and parsing use its native template metadata and canonical parameter values.
For an HTTP item, Templates-only stack inspection supplies the captured project;
the project compatibility matcher checks the item's declared requirements.
Project creation does not run the existing-project item matcher before a
project exists.

The prepared operation binds the selected content, validated values, applicable
compatibility result, and planned outputs. A Compatible result passes only the
capability check; it does not authorize generation or publication. Shared
infrastructure owns preparation and create-only safe publication, including
destination collision checks. Stack content must not depend on replacing user
files or running arbitrary executable actions.

The older [init](../func-init-execution/design.md),
[new](../func-new-execution/design.md), and
[package-install](../template-package-install/design.md) plans contain broader
historical scope. Their persistent stores, lifecycle commands, force behavior,
and Quickstart assumptions are not prerequisites for bounded stack enablement. The
[Templates overview](../templating-system/design.md#historical-and-deferred-documentation-boundaries)
explains that boundary.

### Qualification

Final qualification must exercise project generation, HTTP item generation, and
application execution against the actual installed release. A source/test-built
CLI run alone is insufficient. Record the CLI release/build identity; package
identity, version, digest, and provenance; source revision; OS/RID and toolchain;
selected stack, Azure Functions Runtime, and worker cohort; generated project
and HTTP item; exact commands; and execution results.

Stack/content owners supply stack-specific content and generated-application
qualification. Shared infrastructure/release owners retain shared publication
and platform qualification and assemble the installed-release evidence. Stack
owners do not reimplement or independently qualify the shared publisher.
Existing synthetic tests are starting points, not end-to-end evidence.

| Area | Minimum evidence |
|---|---|
| Package/content validation | Repeatable production of the selected project/HTTP pair; native metadata and exact raw item requirements validate. Malformed, duplicate, or unsupported declarations fail closed; an item with absent requirements remains Incompatible. Record the produced file inventory. |
| Project generation | The shared package path creates the expected project in an empty target, with the selected stack/language and the configuration required by the shared integration contract. |
| HTTP item generation | The shared path adds the expected item to a supported project without overwriting existing files. Collisions and rejected inputs do not modify existing content. |
| Parameters/defaults | List and test every supported native parameter and default for the selected pair. Cover explicit valid values, invalid values, unknown arguments, and unresolved required values. Do not invent parameters to match another stack. |
| Compatibility | Supported reports admit the declared item; missing, Unsupported, conflicting reports and absent/invalid requirements reject it. Test that content requirements and production inspection use the same meanings. |
| Generated application execution | Restore/build as applicable, start the generated app with the Azure Functions Runtime, and invoke its HTTP function. Record the expected response from the chosen content and the actual result. |
| Regression coverage | Preserve ordinary classification, unrelated commands and host-run behavior. Cover B1 malformed inputs and I/O failures, cancellation, immutable capture, and create-only publication failures through the shared path. |
| Shared filesystem/platform qualification | Shared infrastructure/release owners record applicable native checks for regular and special files, links and reparse points, safe publication, and destination collisions. |

Final qualification requires **Windows, Linux, and macOS**. Unix atomic
no-replace runtime validation remains required before claiming Unix support.
Neither unit tests nor a Windows-only run satisfies that requirement.

### Additional Node triggers

Additional Node triggers supply item content, native symbols, requirements, and
trigger-specific dependency and execution evidence. They use the same shared
intake, selection, matching, preparation, and publication path. Reuse Node
inspection only where it establishes the required support. This does not require
new shared orchestration.
