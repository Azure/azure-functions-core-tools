# `func new`

Navigation gateway, not a competing specification. Start with [Templates Architecture](../docs/templates/README.md), then the [proposed behavior contract](../docs/templates/architecture.md) and [implementation baseline and authorization gates](../docs/templates/implementation.md).

## Current behavior

[NewCommand](../src/Func/Commands/NewCommand.cs) retains one positional `[path]`, `--name` / `-n`, `--template` / `-t`, `--force`, `--non-interactive`, `--list` / `-l`, and `--format`, plus inherited/global options. It has no positional template selector, `--path`, `--output` or public dry-run. Its migration handler reports scaffolding and listing unavailable and returns 1 before project file/configuration writes.

[First-run setup](../src/Func/Hosting/FirstRun/FirstRunCoordinator.cs) can complete before that handler and return setup success with rerun guidance; command exit 1 is therefore not unconditional.

The existing [Templater](../src/Func/Templates/Engine/Templater.cs) lists installed templates in a command-scoped session. Command-facing resolution, parameter evaluation, selection and invocation are not wired. Test-only local hive registration is not production registration of workload-delivered templates. Public generation, package lifecycle and grammar changes remain unauthorized; historical specifications and checkboxes are evidence only.
