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

## Status

Preview. The existing initializer scaffolds v4 JavaScript/TypeScript projects.
The stack artifact also carries [native basic project and Node item
templates](NativeTemplates/README.md), registered locally during normal stack
installation. Their staged command journey is not a public `init`/`new` switch;
creation-time npm actions and destructive bundled lifecycle operations remain
explicit limitations. The full owned JavaScript/TypeScript item catalog is
packaged, including hidden known-ID entries. Extension-dependent items remain
unavailable until binding-aware bundle eligibility is qualified; packaging is
not a claim of per-channel workflow availability.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
