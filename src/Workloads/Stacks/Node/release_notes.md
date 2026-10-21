# Azure.Functions.Cli.Workloads.Node

## 1.0.0-preview.2

- Bundle native JavaScript/TypeScript basic project and HTTP item packages inside
  the Node stack artifact, registering declared archives locally during install.
  Template-first commands remain staged; creation-time npm actions and bundled
  replacement/removal are not yet supported.
- Preserve damaged bundled installations, report missing declared archives as
  workload errors, and hold native ownership continuously across workload
  admission, local registration and protected recovery.
- `func init` adopt mode now reports the project language (JavaScript or TypeScript) to the CLI so it gets written to `.func/config.json`.

## 1.0.0-preview.1
- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding and options unchanged. (#5707)
- Package through the shared workload SDK with a generated manifest instead of a checked-in manifest. (#5523)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
