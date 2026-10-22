
@app.service_bus_queue_trigger(arg_name="azservicebus", queue_name="$(SERVICEBUS_NAME_INPUT)",
                               connection="$(CONNECTION_STRING_INPUT)") 
def __FUNCTION_NAME__(azservicebus: func.ServiceBusMessage):
    logging.info('Python ServiceBus Queue trigger processed a message: %s',
                azservicebus.get_body().decode('utf-8'))


# This example uses SDK types to directly access the underlying ServiceBusReceivedMessage object provided by the Service Bus trigger.
# To use, uncomment the section below and add azurefunctions-extensions-bindings-servicebus to your requirements.txt file
# Ref: aka.ms/functions-sdk-servicebus-python
#
# import azurefunctions.extensions.bindings.servicebus as servicebus
# @app.service_bus_queue_trigger(arg_name="receivedmessage",
#                                queue_name="$(SERVICEBUS_NAME_INPUT)",
#                                connection="$(CONNECTION_STRING_INPUT)")
# def __FUNCTION_NAME__(receivedmessage: servicebus.ServiceBusReceivedMessage):
#     logging.info("Python ServiceBus queue trigger processed message.")
#     logging.info("Receiving: %s\n"
#                  "Body: %s\n",
#                  receivedmessage,
#                  receivedmessage.body)
