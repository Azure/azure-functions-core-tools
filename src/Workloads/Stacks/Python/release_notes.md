# Azure.Functions.Cli.Workloads.Python

## 1.0.0-preview.1

- Initial scaffold of the Python workload (entry point + stub project initializer).
- Create a `.venv` (if missing), run `pip install -r requirements.txt`, and set `PYTHON_ISOLATE_WORKER_DEPENDENCIES=1` before host start.
- Honor an activated venv via `$VIRTUAL_ENV` and reuse pre-existing `venv`, `env`, or `.virtualenv` folders before falling back to creating `.venv`.
- Point the host at the venv interpreter via `languageWorkers:python:defaultExecutablePath` and surface the detected Python major.minor as `FUNCTIONS_WORKER_RUNTIME_VERSION` so the worker sees the user's installed packages.
