RESOURCE_METADATA = """
        {
            "author": "John Doe",
            "file": {
                "version": 1.0,
                "releaseDate": "2026-01-01"
            }
        }
        """

@app.mcp_resource_trigger(
    arg_name="context",
    uri="file://readme.md",
    resource_name="readme",
    description="Application readme file",
    mime_type="text/plain",
    metadata=RESOURCE_METADATA
)
def __FUNCTION_NAME__(context: func.MCPToolContext) -> str:
    logging.info("MCP Resource Function triggered with context: %s", context)
    return "# Sample Readme\nThis is a sample readme file for testing MCP Resource Trigger."

