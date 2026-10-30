# Template Engine and Packages

## Purpose

Azure Functions templates are discovered and invoked through the
Microsoft.TemplateEngine integration in `src/Func/Templates/Engine`. Template
packages and their registrations are managed by Template Engine in the shared
func settings hive. They are separate from language worker workloads and
extension bundles.

## Engine integration

- `ITemplaterFactory` creates a command-scoped engine session for a resolved
  `TemplateEngineContext`.
- `Templater` currently lists installed templates by type.
- `TemplateCatalogProjector` maps engine template and package metadata into
  func-owned catalog entries, including `tags.type`, language, identity,
  package information, func host metadata, and parameter definitions.
- Func constraint declarations and their evaluation rules are implemented
  under `src/Func/Templates/Engine` and documented in
  `docs/template-engine-constraints`.
- Raw Template Engine types remain internal to the engine integration.

## Template kinds and command use

Templates use `tags.type` values `item` or `project`. The `func new` command
is designed to consume item templates, while project-template execution belongs
to the `func init` design. Missing or unrecognized type values do not identify
a usable template kind.

The current `func new` command is not yet connected to template selection and
invocation. It reports scaffolding and listing as unavailable with a nonzero
exit code before changing project files. Its planned command flow is described
in `docs/func-new-execution`; package operations are described in
`docs/template-package-install`.

## Package and workload boundaries

Template package discovery and registration use Microsoft.TemplateEngine.
Language workers and extension bundles continue to use the Functions workload
system. Setup does not resolve or install template packages. This keeps
template availability and worker/runtime setup as independent concerns.
