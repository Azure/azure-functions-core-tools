@app.function_name(name="BlobTrigger1")
@app.blob_trigger(arg_name="myblob", path="samples-workitems/{name}",
                  connection="<STORAGE_CONNECTION_SETTING>")
def FunctionName(myblob: func.InputStream):
   return FunctionNameImpl(myblob)