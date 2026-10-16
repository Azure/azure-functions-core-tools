import json
import azure.functions as func
import logging

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)


# An HTTP-Triggered Function with a Durable Functions Client binding
@app.route(route="orchestrators/{functionName}")
@app.durable_client_input(client_name="client")
async def __FUNCTION_NAME___starter(req: func.HttpRequest, client):
    function_name = req.route_params.get('functionName')
    instance_id = await client.start_new(function_name)
    response = client.create_check_status_response(req, instance_id)
    return response


# Orchestrator
@app.orchestration_trigger(context_name="context")
def __FUNCTION_NAME___orchestrator(context):
    result1 = yield context.call_activity("__FUNCTION_NAME___activity", "Seattle")
    result2 = yield context.call_activity("__FUNCTION_NAME___activity", "Tokyo")
    result3 = yield context.call_activity("__FUNCTION_NAME___activity", "London")

    return [result1, result2, result3]

# Activity
@app.activity_trigger(input_name="city")
def __FUNCTION_NAME___activity(city: str):
    return "Hello " + city 