@app.timer_trigger(schedule="$(SCHEDULE_INPUT)", arg_name="myTimer", run_on_startup=False,
              use_monitor=False) 
def __FUNCTION_NAME__(myTimer: func.TimerRequest) -> None:
    
    if myTimer.past_due:
        logging.info('The timer is past due!')

    logging.info('Python timer trigger function executed.')