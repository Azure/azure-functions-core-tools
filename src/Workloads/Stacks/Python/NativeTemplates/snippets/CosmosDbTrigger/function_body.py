
@app.cosmos_db_trigger(arg_name="azcosmosdb", container_name="$(CONTAINER_NAME_INPUT)",
                        database_name="$(DB_NAME_INPUT)", connection="$(CONNECTION_STRING_INPUT)") 
def __FUNCTION_NAME__(azcosmosdb: func.DocumentList):
    logging.info('Python CosmosDB triggered.')
