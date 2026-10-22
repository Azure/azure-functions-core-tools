import datetime
import json
import azure.functions as func
import logging

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)

@app.timer_trigger(schedule="*/10 * * * * *", arg_name="myTimer", run_on_startup=False)
@app.dapr_publish_output(arg_name="pubEvent", pub_sub_name="pubsub", topic="A")
def __FUNCTION_NAME__(myTimer, pubEvent: func.Out[bytes]) -> None:
    """
    Sample Dapr Publish Output Binding
    See https://aka.ms/azure-function-dapr-publish-output-binding for more information about using this binding
    
    These tasks should be completed prior to running :
         1. Install Dapr
    Run the app with below steps
         1. Start function app with Dapr: dapr run --app-id functionapp --app-port 3001 --dapr-http-port 3501 -- func host start
         2. Function will be invoked by Timer trigger and publish messages to message bus.
    """
    logging.info('Python DaprPublish output binding function processed a request.')
    payload = f"Invoked by Timer trigger: Hello, World! The time is {datetime.datetime.now()}"
    pubEvent.set(json.dumps({"payload": payload}).encode('utf-8'))


# Below Azure function will receive message published on topic A, and it will log the message
@app.function_name(name="DaprTopicTriggerFuncApp")
@app.dapr_topic_trigger(arg_name="subEvent", pub_sub_name="pubsub", topic="A")
def main(subEvent) -> None:
    logging.info('Python Dapr Topic Trigger function processed a request from the Dapr Runtime.')
    subEvent_json = json.loads(subEvent)
    logging.info("Topic A received a message: " + subEvent_json["data"])