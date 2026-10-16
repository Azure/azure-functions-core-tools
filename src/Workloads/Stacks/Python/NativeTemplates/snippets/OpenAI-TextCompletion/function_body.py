@app.route(route="__FUNCTION_NAME__", methods=["GET"], auth_level=func.AuthLevel.$(AUTHLEVEL_INPUT))
@app.text_completion_input(arg_name="response", prompt="{Prompt}", max_tokens="100", chat_model="$(CHAT_MODEL_NAME)")
def __FUNCTION_NAME__(req: func.HttpRequest, response: str) -> func.HttpResponse:
    response_json = json.loads(response)
    return func.HttpResponse(response_json["content"], status_code=200)
