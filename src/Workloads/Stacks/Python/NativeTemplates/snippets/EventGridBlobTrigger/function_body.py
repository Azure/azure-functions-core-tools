
@app.blob_trigger(arg_name="myblob", path="$(PATH_TO_BLOB_INPUT)", source="EventGrid",
                               connection="$(CONNECTION_STRING_INPUT)") 
def __FUNCTION_NAME__(myblob: func.InputStream):
    logging.info(f"Python blob trigger (using Event Grid) function processed blob"
                f"Name: {myblob.name}"
                f"Blob Size: {myblob.length} bytes")
