# Templates Architecture

CLI v5 Templates Architecture describes the Template Engine for creating Functions projects with `func init` and adding functions with `func new`.

`func init` filters installed project templates by type and language, selects the unique `func-{stack}` short name, and binds template options from `func.host.json` to native Template Engine parameters. It uses Template Engine dry-run results to validate parameters and detect overwrites before changing the target. If no applicable project templates are installed, it falls back to the existing Stack initializer; if templates exist without a unique default, it fails rather than silently falling back.

Template-driven project initialization is not release-qualified, and `func new` still reports that function generation and template listing are temporarily unavailable.

The first command slice targets .NET 10 isolated/C# with the workload-provided project, HTTP trigger, and Timer trigger templates. Microsoft.TemplateEngine owns discovery and the persistent cache in the CLI hive. Custom constraints, standalone packages through future `func templates` commands, and remote search are separate increments.

- [Architecture](architecture.md) — current foundation, proposed design, and component responsibilities.
- [Implementation](implementation.md) — existing infrastructure, remaining work, and validation.
- [Template package management](package-management.md) — proposed `func templates`
  search, install, and uninstall design, including the dedicated template feed;
  these commands are not yet available.

Earlier designs and investigations are preserved in the [archive](archive/). Their old command names, source-registration plans, helper inventories, and first-slice constraints are historical; use the current architecture and implementation pages rather than treating archived task lists as requirements.
