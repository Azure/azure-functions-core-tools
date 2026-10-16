DEFAULT_CHAT_STORAGE_SETTING = "AzureWebJobsStorage"
DEFAULT_CHAT_COLLECTION_NAME = "ChatState"

# Create a function to get the current weather in a location
@app.function_name("GetWeather")
@app.assistant_skill_trigger(arg_name="location", function_description="Get the current weather in a location")
def get_weather(location: str) -> str:
    logging.info(f"Getting weather for {location}")
    return "The weather is 72 degrees and sunny in " + location    


# Create a function to get the current time of a location using the time zone
@app.function_name("GetTime")
@app.assistant_skill_trigger(arg_name="timeZone", function_description="Get the current time of location using time zone")
def get_time(timeZone: str) -> str:
    logging.info(f"Getting time for {timeZone}")
    
    # Get the timezone object for the passed
    location_timezone = ZoneInfo(timeZone)

    # Get the current time for the passed in timezone
    current_time = datetime.now(location_timezone)

    return json.dumps(current_time.strftime("%H:%M:%S %p"))

# Create assistant function using the assistant create output binding
@app.function_name("CreateAssistant")
@app.route(route="assistants/{assistantId}", methods=["PUT"])
@app.assistant_create_output(arg_name="requests")
def create_assistant(req: func.HttpRequest, requests: func.Out[str]) -> func.HttpResponse:
    assistantId = req.route_params.get("assistantId")
    instructions = """
            Don't make assumptions about what values to plug into functions.
            Ask for clarification if a user request is ambiguous.
            """
    create_request = {
        "id": assistantId,
        "instructions": instructions,
        "chatStorageConnectionSetting": DEFAULT_CHAT_STORAGE_SETTING,
        "collectionName": DEFAULT_CHAT_COLLECTION_NAME
    }
    requests.set(json.dumps(create_request))
    response_json = {"assistantId": assistantId}
    return func.HttpResponse(json.dumps(response_json), status_code=202, mimetype="application/json")

@app.function_name("PostUserQuery")
@app.route(route="assistants/{assistantId}", methods=["POST"])
@app.assistant_post_input(arg_name="state", id="{assistantId}", user_message="{message}", chat_model="$(CHAT_MODEL_NAME)", chat_storage_connection_setting=DEFAULT_CHAT_STORAGE_SETTING, collection_name=DEFAULT_CHAT_COLLECTION_NAME)
def post_user_query(req: func.HttpRequest, state: str) -> func.HttpResponse:
    # Parse the JSON string into a dictionary
    data = json.loads(state)
    # Extract the content of the recentMessage
    recent_message_content = data['recentMessages'][0]['content']
    return func.HttpResponse(recent_message_content, status_code=200, mimetype="text/plain")