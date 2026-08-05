## Why

Template packages and func workloads require different installation semantics and lifecycle ownership. Template packages should be managed through `func new`, while workloads remain managed through `func workload`, with clear guidance when a package is sent to the wrong command.

## What Changes

- Add `func new install <package>` as the canonical template-package install command.
- Add `func new --install <package>` as an equivalent convenience form.
- Delegate package-expression parsing, acquisition, and source identity to Microsoft.TemplateEngine.
- Initially support Microsoft.TemplateEngine's folder and NuGet feed install sources.
- Use `FUNC_CLI_WORKLOADS_SOURCE` as the NuGet feed override for template installs, consistent with `func workload install`.
- Treat a newer package from the same TemplateEngine source as an upgrade, the same version as an idempotent no-op, and an older version as an invalid downgrade.
- Add `--force` for atomically replacing an installed template package with one from a different TemplateEngine source.
- Reject packages that declare both `FuncTemplate` and `FuncCliWorkload` package types as ambiguous.
- Reject template packages passed to `func workload install` with guidance to use `func new install`, and provide reciprocal guidance for workloads passed to `func new install`.

## Capabilities

### New Capabilities

- `template-package-install`: Command surface, package ownership, source-aware installation, upgrades, forced source replacement, and wrong-command guidance for template packages.

### Modified Capabilities

None.

## Impact

- Affects the `func new` and `func workload install` command surfaces.
- Uses the func-owned Microsoft.TemplateEngine package manager and settings hive.
- Shares the existing `FUNC_CLI_WORKLOADS_SOURCE` NuGet feed configuration between template and workload installation.
- Requires package-type validation for `FuncTemplate` and `FuncCliWorkload`.
- Template listing, uninstall, and explicit update commands remain outside this change.
