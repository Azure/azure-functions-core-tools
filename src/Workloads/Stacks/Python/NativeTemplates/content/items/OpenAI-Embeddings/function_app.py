import json
import logging
import azure.functions as func

app = func.FunctionApp()


@app.route(route="__FUNCTION_NAME__", methods=["POST"], auth_level=func.AuthLevel.$(AUTHLEVEL_INPUT))
@app.embeddings_input(arg_name="embeddings", input="{rawText}", input_type="rawText", embeddings_model="$(EMBEDDING_MODEL_NAME)")
def __FUNCTION_NAME__(req: func.HttpRequest, embeddings: str) -> func.HttpResponse:
    import json 
    user_message = req.get_json()
    embeddings_json = json.loads(embeddings)
    embeddings_request = {
        "raw_text": user_message.get("RawText"),
        "file_path": user_message.get("FilePath")
    }
    logging.info(f'Received {embeddings_json.get("count")} embedding(s) for input text '
        f'containing {len(embeddings_request.get("raw_text"))} characters.')
    return func.HttpResponse(status_code=200)