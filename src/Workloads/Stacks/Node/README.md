# Azure Functions CLI – Node workload

This workload extends the Azure Functions CLI (`func`) with Node.js project
support (JavaScript and TypeScript): `func init --stack node` scaffolding,
language-specific options, and (via the workload model) any future Node-only
commands.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Node
# or by alias
func workload install node
```

## Bundled templates

The workload package includes JavaScript and TypeScript project, HTTP trigger,
and timer trigger templates under `content/templates`. The template engine can
discover them through the workload's `templateRoot`. Migrating `func init` and
`func new` to these templates is separate work.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
