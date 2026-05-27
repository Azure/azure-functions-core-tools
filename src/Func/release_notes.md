# Azure Functions CLI 5.0.0

#### Changes

- `func setup` now installs the matching stack workload (Node, Python, Go, DotNet) for the project's worker runtime, detected from `local.settings.json` or selected via prompt / `--features`.
- `func start --no-build`: now actually skips compilation steps in stack workloads. Node skips `npm run build` (still runs `npm install` when `node_modules` is missing), Go skips `go build` (trusts the existing `bin/<module>` binary), Python is a no-op (no build step). Was previously parsed and ignored.
- <entry>
