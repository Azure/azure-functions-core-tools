@app.function_name(name="$(FUNCTION_NAME_INPUT)")
@app.schedule(schedule="$(SCHEDULE_INPUT)", arg_name="myTimer", run_on_startup=True,
              use_monitor=False) 
def $(FUNCTION_NAME_INPUT)(myTimer: func.TimerRequest) -> None:
    utc_timestamp = datetime.datetime.utcnow().replace(
        tzinfo=datetime.timezone.utc).isoformat()

    if myTimer.past_due:
        logging.info('The timer is past due!')

    logging.info('Python timer trigger function ran at %s', utc_timestamp)