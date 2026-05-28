# Getting started with Azure Functions for Python

This project was scaffolded by `func init --stack python`. It uses the
[v2 Python programming model](https://aka.ms/azure-functions/python/v2),
where functions are decorated on a `FunctionApp` instance in
`function_app.py`.

## Project layout

| File / directory      | Purpose                                                                           |
| --------------------- | --------------------------------------------------------------------------------- |
| `function_app.py`     | Entry point. Add functions by decorating handlers on the `app` instance.          |
| `host.json`           | Host-wide configuration. Published with the app.                                  |
| `local.settings.json` | Local-only settings (connection strings, env vars). **Not** published.            |
| `requirements.txt`    | Python packages installed when publishing.                                        |
| `.gitignore`          | Keeps virtual envs, build output, and `local.settings.json` out of source control. |

## Run locally

1. Create and activate a virtual environment:
   ```bash
   python -m venv .venv
   source .venv/bin/activate     # on Windows: .venv\Scripts\activate
   ```
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```
3. Start the host:
   ```bash
   func start
   ```

## Add a function

Edit `function_app.py` and add a decorated handler, for example an HTTP
trigger:

```python
@app.route(route="hello")
def hello(req: func.HttpRequest) -> func.HttpResponse:
    return func.HttpResponse("Hello from Python!")
```

## More

- [Python developer guide](https://aka.ms/azure-functions/python/python-developer-guide)
- [Azure Functions developer guide](https://aka.ms/azure-functions/python/developer-guide)
