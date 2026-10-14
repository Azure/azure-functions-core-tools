# Azure.Functions.Cli.Workloads.Node

## 1.0.0-preview.2

- Bundle native JavaScript/TypeScript basic project and HTTP item packages inside
  the Node stack artifact, registering declared archives locally during install.
  Template-first commands remain staged; creation-time npm actions and bundled
  replacement/removal are not yet supported.
- `func init` adopt mode now reports the project language (JavaScript or TypeScript) to the CLI so it gets written to `.func/config.json`.

## 1.0.0-preview.1
