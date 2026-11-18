# `func new`

Navigation gateway, not a competing specification. Start with [Templates Architecture](../docs/templates/README.md), then the [proposed behavior contract](../docs/templates/architecture.md) and [implementation baseline and authorization gates](../docs/templates/implementation.md).

## Current behavior

[NewCommand](../src/Func/Commands/NewCommand.cs) retains one positional `[path]`, `--name` / `-n`, `--template` / `-t`, `--force`, `--non-interactive`, `--list` / `-l`, and `--format`, plus inherited/global options. It has no positional template selector, `--path`, `--output` or public dry-run. It requires `.func/config.json` and otherwise directs the user to run `func init`; it never initializes a project. `--list` reports listing unavailable and returns 1.

`func new` participates in first-run setup like `func init`, except that `--non-interactive`, `--list`, and `-l` skip setup.

The existing [Templater](../src/Func/Templates/Engine/Templater.cs) lists installed templates in a command-scoped session, including those exposed from workload `templateRoot` paths. Command-facing resolution and instantiation are wired for `func init` (project template by language, with host metadata binding its options) and `func new` (item templates by project language and an optional identity, name or short-name reference). Workload packaging does not emit `templateRoot` yet. Package lifecycle and grammar changes remain unauthorized; historical specifications and checkboxes are evidence only.
