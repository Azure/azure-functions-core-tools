# Python Functions project

This project uses the Python v2 programming model. Files named `function_*.py`
in the `functions` directory are imported at startup, and their `bp` blueprints
are registered with the app. Add functions directly to `function_app.py` or
add blueprint modules to that directory.

To use an item template in an existing project without this discovery setup,
import its `bp` from the generated module and call `app.register_functions(bp)`
in `function_app.py`. Register each blueprint separately.

See the [Python developer reference](https://learn.microsoft.com/en-us/azure/azure-functions/functions-reference-python?pivots=python-mode-decorators)
for more information.

Create a Python virtual environment, install `requirements.txt`, and run
`func run`. Timer triggers require `AzureWebJobsStorage`; the local setting uses
Azurite by default. Keep `local.settings.json` out of source control.
