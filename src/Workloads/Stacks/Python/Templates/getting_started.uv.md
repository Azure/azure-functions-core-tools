# Getting started with Azure Functions for Python (uv)

This project was scaffolded by `func init --stack python` and is managed with
[uv](https://docs.astral.sh/uv/). It uses the
[v2 Python programming model](https://aka.ms/azure-functions/python/v2),
where functions are decorated on a `FunctionApp` instance in
`function_app.py`.

For a new project, `func init --stack python` selects uv when it is available
and pip otherwise. Use `--package-manager uv` or `--package-manager pip` to
override the default.

## Project layout

- `function_app.py`: entry point. Add functions by decorating handlers on the `app` instance.
- `pyproject.toml`: project metadata, supported Python versions, dependencies,
  and the `[tool.uv]` table that identifies this as a uv project.
- `.python-version`: the Python version uv uses for this project.
- `uv.lock`: exact resolved dependency versions. Created on first run; commit it.
- `requirements.txt`: exported from `uv.lock` for pip-based builds. Keep it in
  sync with the lockfile.
- `host.json`: host-wide configuration. Published with the app.
- `local.settings.json`: local-only settings (connection strings, env vars). **Not** published.

## Run locally

1. [Install uv](https://docs.astral.sh/uv/getting-started/installation/).
   You don't need to install Python yourself; uv downloads the version in
   `.python-version` on demand.
2. Start the host:
   ```bash
   func run
   ```
   `func run` runs the exact `uv sync` command, which creates `.venv` and
   installs the dependencies for the project, then exports `requirements.txt`
   from `uv.lock`.

You can also start the host through uv:

```bash
uv run func run
```

`func` must be installed separately (it is not a Python package).

`uv run` syncs inexactly by default, while the `uv sync` invoked by `func run`
is exact. As a result, `func run` is not guaranteed to be a no-op after running
`uv run`; it may remove packages installed only for extras or dependency groups
passed to `uv run`.

The CLI recognizes uv projects when `uv.lock` exists or `pyproject.toml`
contains `[tool.uv]` or a `[tool.uv.*]` table. A `poetry.lock` without
`uv.lock` selects pip, and generic `[project]` metadata or `[tool.uv.pip]`
settings alone do not select uv. Setting `managed = false` under `[tool.uv]`
opts the project out of uv and back to the pip flow, even when `uv.lock`
exists.

## Deployment requirements

The exported requirements use `uv export --frozen --no-dev --no-hashes
--no-editable`. Packaged projects include their root as a non-editable
requirement; virtual projects omit it. A pip-based build must have the project
source, build metadata, and build-backend dependencies in its build context to
build/install that root project. Uploading a zip alone does not necessarily
build or install the project.

## Add a dependency

```bash
uv add requests
```

The next `func run` picks it up and refreshes `requirements.txt`.

## Change the Python version

```bash
uv python pin 3.12
```

The Python worker supports different versions on different platforms (for
example, only 3.15 on Windows ARM64). `func run` checks the pinned version
before starting and suggests a supported one if needed.

## Use an internal package feed

uv ignores pip settings such as `pip.conf` and `PIP_INDEX_URL`. If packages
must come from an internal feed:

- Set the feed as uv's default index in your user-level `uv.toml`, or set
  `UV_DEFAULT_INDEX`.
- Configure credentials with `UV_KEYRING_PROVIDER` or
  `UV_INDEX_<NAME>_USERNAME` / `UV_INDEX_<NAME>_PASSWORD`.
- If Python downloads from GitHub are blocked, install Python yourself and set
  `UV_PYTHON_DOWNLOADS=never`, or set `UV_PYTHON_INSTALL_MIRROR`.
- Run `uv lock` after switching feeds, because `uv.lock` records index URLs.

## Troubleshooting

- **`uv run func run` fails with "program not found"**: `func` isn't on PATH.
  Install the Azure Functions CLI and make sure `func` runs on its own.
- **uv fails to download Python**: check your network or proxy settings, or
  install the pinned version yourself and run `uv python pin` with a version
  you have.
- **`uv sync` fails to resolve dependencies**: run `uv sync` in this folder to
  see uv's full diagnostics, then fix the conflicting requirement in
  `pyproject.toml`.
- **"No Python interpreter found"**: `UV_PROJECT_ENVIRONMENT` points to a folder
  without an environment. Unset it or re-run `uv sync`.
- **"Python X is not supported"**: pin the version suggested in the message
  with `uv python pin <version>`, then run `uv sync`.
- **`uv sync` fails with a network, 401, or 403 error**: uv can't reach the
  package index. See "Use an internal package feed" above.

## More

- [uv documentation](https://docs.astral.sh/uv/)
- [Python developer guide](https://aka.ms/azure-functions/python/python-developer-guide)
- [Azure Functions developer guide](https://aka.ms/azure-functions/python/developer-guide)
