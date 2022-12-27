import logging
import azure.functions as func
app = func.FunctionApp()
@app.function_name(name="QueueTrigger1")
@app.queue_trigger(arg_name="msg", queue_name="python-queue-items",
                   connection="")  
def test_function(msg: func.QueueMessage):
    logging.info('Python EventHub trigger processed an event: %s',
                 msg.get_body().decode('utf-8'))