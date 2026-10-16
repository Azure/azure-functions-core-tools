
@app.queue_trigger(arg_name="azqueue", queue_name="$(QUEUE_NAME_INPUT)",
                               connection="$(CONNECTION_STRING_INPUT)") 
def __FUNCTION_NAME__(azqueue: func.QueueMessage):
    logging.info('Python Queue trigger processed a message: %s',
                azqueue.get_body().decode('utf-8'))
