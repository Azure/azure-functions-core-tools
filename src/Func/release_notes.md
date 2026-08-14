## 5.0.0-preview.3

# Azure Functions CLI 5.0.0

#### Changes

- `func init` now adopts an existing project's language (writes `stack.language` to `.func/config.json`) using the runtime project resolver. (#5300)
- `func init` heals a `.func/config.json` that has `stack.runtime` but no `stack.language` on a multi-language stack, instead of refusing with "pass --force". Other top-level keys (profiles, etc.) are preserved. (#5300)
- Clarified the `func new` "missing language" hint to mention both scaffolding and adopting an existing project. (#5300)
- Fix `func start` failing to resolve installed prerelease worker workloads against built-in profile ranges (e.g. `node [3.13.0]` now accepts `3.13.0-preview.1`). (#5286)
- Fix `func new` printing the "Cannot determine language" error three times when `stack.language` is missing from `.func/config.json`. (#5306)
- When no installed stack workload matches the project, the error now includes specific `func workload install` guidance for the matching stack. (#5508)
- Workload manifests now support a "rid-pointer" kind that maps runtime identifiers to platform-specific implementation packages, enabling per-OS/arch workload resolution. (#5516)
- When managed Azurite is already running on the expected ports, `func start` now identifies the owning process and classifies it by data directory — reusing it if it matches, or failing with clear PID/directory guidance if it serves a different store. (#5264)
- Managed Azurite now detects HTTP 500 responses paired with the Azurite-Blob header and surfaces a live warning with data-directory reset guidance, repeated in the end-of-run summary. (#5263)
- `func workload install <path>.nupkg` now gives a clear "file does not exist" error when the package file is missing, instead of falling through to catalog resolution with a confusing failure. (#5497)

