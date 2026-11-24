import azure.functions as func

# In projects without blueprint discovery, import and register bp in function_app.py.
bp = func.Blueprint()


@bp.function_name(name="function_FunctionName")
@bp.route(route="function_FunctionName", auth_level=func.AuthLevel.FUNCTION)
def main(req: func.HttpRequest) -> func.HttpResponse:
    return func.HttpResponse("Hello, world!")
