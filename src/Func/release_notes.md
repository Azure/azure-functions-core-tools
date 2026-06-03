# Azure Functions CLI 5.0.0

#### Changes

- Fix `func start` failing to resolve installed prerelease worker workloads against built-in profile ranges (e.g. `node [3.13.0]` now accepts `3.13.0-preview.1`). (#5286)
- Fix `func new` printing the "Cannot determine language" error three times when `stack.language` is missing from `.func/config.json`. (#5306)

