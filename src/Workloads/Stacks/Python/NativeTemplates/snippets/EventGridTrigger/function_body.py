@app.event_grid_trigger(arg_name="azeventgrid")
def __FUNCTION_NAME__(azeventgrid: func.EventGridEvent):
    logging.info('Python EventGrid trigger processed an event')
