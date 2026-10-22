
@app.event_hub_message_trigger(arg_name="azeventhub", event_hub_name="$(EVENTHUB_NAME_INPUT)",
                               connection="$(CONNECTION_STRING_INPUT)") 
def __FUNCTION_NAME__(azeventhub: func.EventHubEvent):
    logging.info('Python EventHub trigger processed an event: %s',
                azeventhub.get_body().decode('utf-8'))


# This example uses SDK types to directly access the underlying EventData object provided by the Event Hubs trigger.
# To use, uncomment the section below and add azurefunctions-extensions-bindings-eventhub to your requirements.txt file
# Ref: aka.ms/functions-sdk-eventhub-python
#
# import azurefunctions.extensions.bindings.eventhub as eh
# @app.event_hub_message_trigger(
#     arg_name="event", event_hub_name="$(EVENTHUB_NAME_INPUT)", connection="$(CONNECTION_STRING_INPUT)"
# )
# def __FUNCTION_NAME__(event: eh.EventData):
#     logging.info(
#         "Python EventHub trigger processed an event %s",
#         event.body_as_str()
#     )
