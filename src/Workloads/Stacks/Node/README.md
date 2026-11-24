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
`func new` to these templates is separate work. HTTP triggers support function,
anonymous, and admin authorization levels, defaulting to function; their
templates declare `--auth-level` and `-a` aliases for future CLI integration.
Project settings use Azurite for local storage so timer triggers can start locally.
The project templates declare an npm restore post-action for the func host; a
Node restore processor is not yet implemented. The action is skipped by
`dotnet new`, which uses the same action ID for NuGet restore.

## Template testing

From the repository root, run `node --test test/Workloads/Templates/Node.Tests/NodeTemplateBuild.test.mjs`
to check TypeScript project rebuilds. The test requires the .NET SDK, Node.js,
and npm. To reuse an existing installation of the project dependencies, set
`NODE_TEMPLATE_TEST_NODE_MODULES` to its `node_modules` directory.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
