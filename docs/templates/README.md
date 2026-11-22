# Templates Architecture

CLI v5 Templates Architecture describes the Template Engine for creating Functions projects with `func init` and adding functions with `func new`. This branch contains a local `func init` proof of concept.

`func init` filters installed project templates by type and language, selects the unique `func-{stack}` short name, and binds template options from `func.host.json` to native Template Engine parameters. It uses Template Engine dry-run results to validate parameters and detect overwrites before changing the target. If no applicable project templates are installed, it falls back to the existing Stack initializer; if templates exist without a unique default, it fails rather than silently falling back.

This is not release-qualified: workload installation does not yet register production template folders in the CLI hive, and `func new` remains unavailable.

- [Architecture](architecture.md) — current foundation, proposed design, and component responsibilities.
- [Implementation](implementation.md) — existing infrastructure, remaining work, and validation.

Earlier designs and investigations are preserved in the [archive](archive/).
