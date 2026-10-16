@app.mcp_tool()
def __FUNCTION_NAME__(context: func.MCPToolContext) -> None:
    """
    A simple function that returns a greeting message.
    """
    return "Hello I am MCPTool! Called with context: " + str(context)
