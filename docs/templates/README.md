# Templates Architecture

CLI v5 Templates Architecture describes the proposed Template Engine for creating Functions projects with `func init` and adding functions with `func new`.

Discovery returns `FuncTemplateInfo` values with parsed `func.host.json` metadata; `.Template` exposes the original native `ITemplateInfo`. `func init` selects a project template by language, binds its options, and invokes the engine, falling back to the existing Stack initializer when no template matches. Metadata loading does not add constraint evaluation. `func new` requires an initialized project and invokes a matching item template; template listing remains temporarily unavailable. The .NET template content and workload packaging are added separately.

- [Architecture](architecture.md) — current foundation, proposed design, and component responsibilities.
- [Implementation](implementation.md) — existing infrastructure, remaining work, and validation.

Earlier designs and investigations are preserved in the [archive](archive/).
