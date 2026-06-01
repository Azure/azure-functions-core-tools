# Azure Functions CLI 5.0.0

#### Changes

- `func setup` now installs the matching stack workload (Node, Python, Go, DotNet) for the project's worker runtime, detected from `local.settings.json` or selected via prompt / `--features`.
- `func start --no-build`: now actually skips compilation steps in stack workloads. Node skips `npm run build` (still runs `npm install` when `node_modules` is missing), Go skips `go build` (trusts the existing `bin/<module>` binary), Python is a no-op (no build step). Was previously parsed and ignored.
- `func start`: `local.settings.json` Values no longer overwrite environment variables already set in the current shell, matching v4 behavior. Skipped keys are logged as warnings, along with empty keys. Empty string values are preserved so the host can see an intentional clear.
- `func new`: sanitize the function-name token when rendering v2 templates so names like `HttpTrigger-Python` produce valid Python/Node identifiers instead of `SyntaxError` (#5202).
- `func setup --features python` now installs only the python worker build for the current RID (#5247), matching the host workload resolution.
- `func workload install <alias>` now resolves to the per-RID workload pack for the current platform when an alias spans multiple per-RID packs (e.g. `host`, `python-worker`). Installing by full package id still installs that exact package. Running `func setup` for Python on `win-arm64` now reports a clear unsupported-runtime error instead of failing later in the install flow.
- <entry>
