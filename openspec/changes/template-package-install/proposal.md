## Why

Template packages and func workloads require different lifecycle semantics and ownership. Template packages should be managed through `func new`, while workloads remain managed through `func workload`, with clear guidance when a package is sent to the wrong command.

## What Changes

- Add `func new install <package>` as the canonical template-package install command.
- Add `func new --install <package>` as an equivalent convenience form.
- Add `--source <feed>` to template install commands for an explicit NuGet feed override.
- Add `func new uninstall <package>` as the canonical template-package uninstall command.
- Add `func new --uninstall <package>` as an equivalent convenience form.
- Add `func new update <package>` as the canonical command for updating an installed template package.
- Add `func new --update <package>` as an equivalent convenience form.
- Allow `--source <feed>` on template update commands for an explicit NuGet feed override.
- Delegate package-expression parsing, acquisition, and source identity to Microsoft.TemplateEngine.
- Initially support Microsoft.TemplateEngine's folder and NuGet feed install sources.
- Resolve the NuGet feed for install and update from `--source`, then `FUNC_CLI_WORKLOADS_SOURCE`, consistent with `func workload install`.
- Replace an installed package with an explicitly resolved newer or older version from the same TemplateEngine source, while treating the same version as an idempotent no-op.
- Add `--force` for atomically replacing an installed template package with one from a different TemplateEngine source.
- Reject packages that declare both `FuncTemplate` and `FuncCliWorkload` package types as ambiguous.
- Reject template packages passed to `func workload install` with guidance to use `func new install`, and provide reciprocal guidance for workloads passed to `func new install`.

## Capabilities

### New Capabilities

- `template-package-install`: Command surface, package ownership, source-aware installation, update, forced source replacement, uninstall, and wrong-command guidance for template packages.

### Modified Capabilities

None.

## Impact

- Affects the `func new` and `func workload install` command surfaces.
- Uses the func-owned Microsoft.TemplateEngine package manager and settings hive.
- Shares the existing `--source` and `FUNC_CLI_WORKLOADS_SOURCE` NuGet feed configuration model between template and workload installation.
- Requires package-type validation for `FuncTemplate` and `FuncCliWorkload`.
- Template listing remains outside this change.
