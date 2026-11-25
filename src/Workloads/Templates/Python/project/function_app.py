from importlib import import_module
from pathlib import Path

import azure.functions as func

app = func.FunctionApp(http_auth_level=func.AuthLevel.FUNCTION)

# Each module in functions exposes a blueprint that the app registers at startup.
for module_path in sorted((Path(__file__).parent / "functions").glob("function_*.py")):
    module = import_module(f"functions.{module_path.stem}")
    app.register_functions(module.bp)
