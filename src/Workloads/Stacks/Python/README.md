# Azure Functions CLI – Python workload

This workload extends the Azure Functions CLI (`func`) with Python project
support: `func init --stack python` scaffolding, language-specific options,
and (via the workload model) any future Python-only commands.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Python
# or by alias
func workload install python
```

## Status

Preview. `func init --stack python` scaffolds a v2 programming model app, and
`func run` prepares the Python environment (uv or pip) before starting the host.

## Using uv

[uv](https://docs.astral.sh/uv/) installs Python on demand, creates the virtual
environment and installs packages from `pyproject.toml` / `uv.lock`.

### Creating a project

```bash
func init --stack python                          # uv if installed, otherwise pip
func init --stack python --package-manager uv     # explicitly choose uv
func init --stack python --package-manager pip    # explicitly choose pip
```

The initializer uses uv when it is on PATH; otherwise it creates a pip project.
Use `--package-manager` to override that default.
A uv project gets `pyproject.toml` (depends on `azure-functions`,
`requires-python = ">=3.10"`, and an empty `[tool.uv]` table) and
`.python-version` instead of `requirements.txt`. Existing files are
kept unless `--force` is passed.

The `.python-version` pin depends on the platform, because the Python worker
ships a payload only for some versions on some platforms. func pins `3.13`
when the platform supports it, otherwise the newest supported version (`3.15`
on Windows ARM64, whose worker ships only 3.15). A project pinned on one
platform may need a different pin on another.

### Running a project

```bash
func run
uv run func run
```

`func run` runs the exact `uv sync` command for uv projects, then starts the
host with the project's Python environment. By default, `uv run` syncs the
environment inexactly, so the exact sync inside `func run` is not guaranteed
to be a no-op. In particular, it can remove packages that were installed only
for extras or dependency groups passed to `uv run`.

`func run` selects uv when `uv.lock` exists, or when `pyproject.toml` contains
`[tool.uv]` or a `[tool.uv.*]` table. Without `uv.lock`, `poetry.lock` takes
precedence and selects pip. Generic `[project]` metadata alone does not select
uv, and neither do `[tool.uv.pip]` settings, which only configure the
`uv pip` interface. Other projects use pip, reusing an active `VIRTUAL_ENV` or
conventional `.venv` when available. Pip installs from `requirements.txt` only
when that file exists.

To keep a project on the pip flow even though it has `uv.lock` or other uv
settings, opt out of uv project management:

```toml
[tool.uv]
managed = false
```

`func run` then never invokes `uv sync` or `uv export`. The setting must appear
as `managed = false` directly under the `[tool.uv]` header.

If the project uses uv but uv is not on PATH, `func run` warns and falls back to
pip when `requirements.txt` exists; otherwise it fails with install guidance.

`UV_PROJECT_ENVIRONMENT` is respected for the environment location.

Before starting the host, `func run` checks the environment's Python version
against the installed Python worker: a version is supported only when the worker
has `<version>/<OS>/<Arch>/worker.py` for the current platform. If the worker
payload can't be read, func uses a built-in table (Windows ARM64: 3.15; other
platforms: 3.10-3.15). Pip projects get the same check.

### Restricted package feeds

uv does not read pip settings (`pip.conf`, `PIP_INDEX_URL`), so a machine where
pip installs from an internal feed can still fail `uv sync`. func passes uv's
own configuration and `UV_*` variables through unchanged and adds no index,
credential, or download flags. To use an internal feed:

- Set the feed as uv's default index in user-level `uv.toml`
  (`%APPDATA%\uv\uv.toml` or `~/.config/uv/uv.toml`) or with `UV_DEFAULT_INDEX`:

  ```toml
  [[index]]
  name = "internal"
  url = "https://pkgs.example.com/simple/"
  default = true
  ```

- Configure authentication with `UV_KEYRING_PROVIDER=subprocess` and a keyring
  plugin, or `UV_INDEX_INTERNAL_USERNAME` / `UV_INDEX_INTERNAL_PASSWORD`.
- uv downloads managed Python from GitHub. Where that is blocked, install
  Python yourself and set `UV_PYTHON_DOWNLOADS=never`, or point
  `UV_PYTHON_INSTALL_MIRROR` at a mirror.
- `uv.lock` records index URLs. Re-run `uv lock` after switching feeds.

A blocked sync stops before the host starts with `'uv sync' failed`; uv's own
error is shown above it.

### requirements.txt export

After a successful sync, func exports dependencies with
`uv export --frozen --no-dev --no-hashes --no-editable`. For packaged projects,
the export includes the project root as a non-editable requirement; for virtual
projects, it omits the root. This lets pip-based build systems install the
application when they have its source, build metadata, and build-backend
dependencies in the build context. Uploading a zip by itself does not
necessarily build or install the project. Keep the exported `requirements.txt`
in sync with `uv.lock`; a hand-written file is not overwritten.

### Troubleshooting

Errors raised by func:

- **uv was not found on PATH and this project has no requirements.txt** — install
  uv (https://docs.astral.sh/uv/getting-started/installation/) or add a `requirements.txt`.
- **'uv sync' failed (exit N)** — run `uv sync` in the project folder to see uv's full output.
- **No Python interpreter found at …** — check `UV_PROJECT_ENVIRONMENT` or re-run `uv sync`.
- **Python X.Y is not supported by the Azure Functions Python worker on &lt;rid&gt;
  (supported: …)** — run `uv python pin <suggested version>` and `uv sync`. For
  pip projects, recreate the virtual environment with a supported version.
- **Python X.Y is newer than the latest version known to be supported** — warning
  only, shown when the worker payload can't be read; the host may fail to start
  the worker. Pin a supported version if it does.
- **requirements.txt was not generated by uv and was left unchanged** — delete it
  to let func regenerate it, or keep it in sync manually.

Errors raised by uv (shown before func starts when using `uv run func run`):

- **No interpreter found / download failed** — check network access, or set
  `UV_PYTHON_DOWNLOADS` and install Python yourself.
- **Dependency resolution failed** — fix the package name or version in `pyproject.toml`.
- **Package or Python download blocked** — configure uv for your internal feed
  (see [Restricted package feeds](#restricted-package-feeds)).
- **`func` not found** — `uv run` uses your PATH; install the Azure Functions CLI globally.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
