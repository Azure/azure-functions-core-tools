# Azure.Functions.Cli.Workloads.Python

## 1.0.0-preview.3

- Add uv support for Python function apps. `func init --stack python` scaffolds `pyproject.toml` and `.python-version` when uv is on PATH; `--package-manager uv|pip` overrides detection.
- `func run` detects uv projects from `uv.lock` or `[tool.uv]` tables (with `poetry.lock` taking precedence when no `uv.lock` exists; `[tool.uv.pip]` settings are ignored and `[tool.uv] managed = false` opts out), runs `uv sync`, and exports `requirements.txt` with packaged roots retained non-editably. Falls back to pip with a warning when uv is not installed.
- `uv run func run` is supported; func uses the same uv project environment.
- Clear errors for missing uv, failed `uv sync`, missing interpreters, and unsupported Python versions.
- Python versions are validated per platform against the installed worker payload, and the scaffolded `.python-version` pin is platform-aware (3.15 on `win-arm64`, 3.13 elsewhere).

## 1.0.0-preview.2

- Register shared stack and language metadata through `IProjectStack`, keeping existing `func init` scaffolding and options unchanged. (#5707)
- Package through the shared workload SDK with a generated manifest instead of a checked-in manifest. (#5523)
- **Compatibility:** Requires Azure Functions CLI 5.0.0-preview.4 or later for the new `IProjectStack` contract; older CLIs cannot safely load this binary.
