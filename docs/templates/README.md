# Templates Architecture

CLI v5 Templates Architecture describes the proposed Template Engine for creating Functions projects with `func init` and adding functions with `func new`. Existing Stack initializers remain operative; the current `func new` handler reports scaffolding and template listing temporarily unavailable.

- [Architecture](architecture.md) — the Template Engine, its command-facing model, and required behavior.
- [Implementation](implementation.md) — available infrastructure, work to complete, and validation.

The new Template Engine and command integration are proposed and not yet complete. Existing project initializers remain in use until their replacements are validated.

Earlier designs and investigations are preserved in the [archive](archive/). They are background material, not an additional implementation specification.
