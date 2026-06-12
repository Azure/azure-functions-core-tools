# Azure Functions CLI

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [5.0.0]

### Changed

- `func init` now adopts an existing project's language (writes `stack.language` to `.func/config.json`) using the runtime project resolver. (#5300)
- `func init` heals a `.func/config.json` that has `stack.runtime` but no `stack.language` on a multi-language stack, instead of refusing with "pass --force". Other top-level keys (profiles, etc.) are preserved. (#5300)
- Clarified the `func new` "missing language" hint to mention both scaffolding and adopting an existing project. (#5300)

### Fixed

- `func start` failing to resolve installed prerelease worker workloads against built-in profile ranges (e.g. `node [3.13.0]` now accepts `3.13.0-preview.1`). (#5286)
- `func new` printing the "Cannot determine language" error three times when `stack.language` is missing from `.func/config.json`. (#5306)
