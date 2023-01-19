@app.function_name(name="FunctionName")
@app.route(route="FunctionName")
def FunctionName(req: func.HttpRequest) -> func.HttpResponse:
     return FunctionNameImpl(req)  