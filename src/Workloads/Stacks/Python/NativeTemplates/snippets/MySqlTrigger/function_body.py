
@app.mysql_trigger(arg_name="changes", table_name="$(TABLE_NAME_INPUT)",
                        connection_string_setting="$(CONNECTION_STRING_INPUT)") 
def __FUNCTION_NAME__(changes: str):
    if changes:
        logging.info("MySQL Changes: ")    
    json_changes = json.loads(changes)
    for change in json_changes:
        rowdata = func.MySqlRow(change["Item"]) 
        logging.info(rowdata.data)
