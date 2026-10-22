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
Dependency installation is best effort. If npm is unavailable or returns a
failure, initialization retains the generated files and warns you to run
`npm install` in the project directory. `--skip-npm-install` skips that step
without a dependency warning.
The stack artifact carries [two native basic project
templates](NativeTemplates/README.md), registered locally during normal stack
installation. All 33 owned function items are produced separately, including
both HTTP languages and hidden known-ID entries. The Node workload qualifies
their selected sources through the neutral loaded-owner policy boundary.
The separate package identity does not grant production publisher or external
association authority. Their staged command journey is not a public `init`/`new` switch;
creation-time npm actions and destructive bundled lifecycle operations remain
explicit limitations. Preparation ownership migration also requires the neutral
post-configuration completion contract. Extension-dependent items remain
unavailable until binding-aware bundle eligibility is qualified; packaging is
not a claim of per-channel workflow availability.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
