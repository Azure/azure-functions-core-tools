# Templates Architecture

CLI v5 Templates Architecture describes the proposed Template Engine for creating Functions projects with `func init` and adding functions with `func new`.

The discovery foundation is not yet integrated with `func init` or `func new`. `func init` continues to use the existing Stack initializers; `func new` reports that function generation and template listing are temporarily unavailable.

The first command slice targets .NET 10 isolated/C#/HTTP with workload-provided templates. Microsoft.TemplateEngine owns discovery and the persistent cache in the CLI hive. Custom constraints, standalone packages through future `func templates` commands, and remote search are separate increments.

- [Architecture](architecture.md) — current foundation, proposed design, and component responsibilities.
- [Implementation](implementation.md) — existing infrastructure, remaining work, and validation.

Earlier designs and investigations are preserved in the [archive](archive/). Their old command names, source-registration plans, helper inventories, and first-slice constraints are historical; use the current architecture and implementation pages rather than treating archived task lists as requirements.
