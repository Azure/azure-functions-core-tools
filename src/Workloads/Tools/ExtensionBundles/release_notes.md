# Azure.Functions.Cli.Workloads.ExtensionBundles

## 4.35.0.1

- Repackage as a content workload (`kind: content`). No entry-point assembly; the bundle payload ships under `tools/any/`.
- Pins extension bundle 4.35.0.
- Workload pkg version encodes both the bundle payload version and a per-bundle iteration counter. `$(BundleChannel)` selects the CDN bundle id at pack time (stable | preview | experimental).
