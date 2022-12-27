import logging
import azure.functions as func
app = func.FunctionApp()
@app.function_name(name="ServiceBusQueueTrigger1")
@app.service_bus_queue_trigger(arg_name="msg", queue_name="myinputqueue", connection="")
def test_function(msg: func.ServiceBusMessage):
    logging.info('Python ServiceBus queue trigger processed message: %s',
                 msg.get_body().decode('utf-8'))