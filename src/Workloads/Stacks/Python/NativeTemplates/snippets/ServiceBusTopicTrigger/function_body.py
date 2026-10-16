
@app.service_bus_topic_trigger(arg_name="azservicebus", subscription_name="$(SERVICEBUS_SUBSCRIPTION_NAME_INPUT)", topic_name="$(SERVICEBUS_NAME_INPUT)",
                               connection="$(CONNECTION_STRING_INPUT)") 
def __FUNCTION_NAME__(azservicebus: func.ServiceBusMessage):
    logging.info('Python ServiceBus Topic trigger processed a message: %s',
                azservicebus.get_body().decode('utf-8'))


# This example uses SDK types to directly access the underlying ServiceBusReceivedMessage object provided by the Service Bus trigger.
# To use, uncomment the section below and add azurefunctions-extensions-bindings-servicebus to your requirements.txt file
# Ref: aka.ms/functions-sdk-servicebus-python
#
# import azurefunctions.extensions.bindings.servicebus as servicebus
# @app.service_bus_topic_trigger(arg_name="receivedmessage",
#                                topic_name="$(SERVICEBUS_NAME_INPUT)",
#                                connection="$(CONNECTION_STRING_INPUT)",
#                                subscription_name="$(SERVICEBUS_SUBSCRIPTION_NAME_INPUT)")
# def __FUNCTION_NAME__(receivedmessage: servicebus.ServiceBusReceivedMessage):
#     logging.info("Python ServiceBus topic trigger processed message.")
#     logging.info("Receiving: %s\n"
#                  "Body: %s\n",
#                  receivedmessage,
#                  receivedmessage.body)
