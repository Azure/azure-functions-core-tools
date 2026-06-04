# Azure Functions CLI 5.0.0

#### Changes

- `func init` now adopts an existing project's language (writes `stack.language` to `.func/config.json`) using the runtime project resolver. (#5300)
- `func init` heals a `.func/config.json` that has `stack.runtime` but no `stack.language` on a multi-language stack, instead of refusing with "pass --force". Other top-level keys (profiles, etc.) are preserved. (#5300)
- Clarified the `func new` "missing language" hint to mention both scaffolding and adopting an existing project. (#5300)
- Fix `func start` failing to resolve installed prerelease worker workloads against built-in profile ranges (e.g. `node [3.13.0]` now accepts `3.13.0-preview.1`). (#5286)
- Fix `func new` printing the "Cannot determine language" error three times when `stack.language` is missing from `.func/config.json`. (#5306)

