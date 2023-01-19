@app.function_name(name = "FunctionName")
@app.blob_trigger(arg_name = "myblob", path = "samples-workitems/{name}",
                  connection = "<STORAGE_CONNECTION_SETTING>")
def FunctionName_function(myblob: func.InputStream):
   logging.info(f"Python blob trigger function processed blob \n"
                f"Name: {myblob.name}\n"
                f"Blob Size: {myblob.length} bytes")