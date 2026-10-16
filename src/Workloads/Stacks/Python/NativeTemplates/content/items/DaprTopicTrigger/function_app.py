import json
import azure.functions as func
import logging

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)

@app.dapr_topic_trigger(arg_name="subEvent", pub_sub_name="pubsub", topic="A")
@app.dapr_state_output(arg_name="state", state_store="statestore", key="order")
def __FUNCTION_NAME__(subEvent, state: func.Out[str]) -> None:
    """
    Sample Dapr topic trigger with dapr_state_output
    See https://aka.ms/azure-functions-dapr for more information about using this binding
    
    These tasks should be completed prior to running :
         1. Install Dapr
    Run the app with below steps
         1. Start function app with Dapr: dapr run --app-id functionapp --app-port 3001 --dapr-http-port 3501 -- func host start
         2. Invoke function app: dapr publish --pubsub pubsub --publish-app-id functionapp --topic A --data '{"value": { "orderId": "1234" } }'
    """
    logging.info('Python DaprTopic trigger with DaprState output binding function processed a request from the Dapr Runtime.')
    subEvent_json = json.loads(subEvent)
    state.set(str(subEvent_json["data"]))