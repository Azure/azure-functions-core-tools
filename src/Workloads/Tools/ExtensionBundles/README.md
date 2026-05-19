# Azure Functions CLI – Extension Bundles workload

This workload resolves Azure Functions extension bundles for `func start` and
owns the on-disk bundle payload cache. It is the single acquisition mechanism
for extension bundles in v5.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.ExtensionBundles
# or by alias
func workload install bundles
```

## Status

Preview, scaffolding only. The resolver, on-disk cache, and `func start`
integration land in a follow-up PR. See the
[bundles workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/bundles-workload-spec.md).

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Bundles workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/bundles-workload-spec.md)
