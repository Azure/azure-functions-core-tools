@app.function_name(name="FunctionName")
@app.schedule(schedule="0 */5 * * * *", arg_name="mytimer", run_on_startup=True,
              use_monitor=False) 
def FunctionName(mytimer: func.TimerRequest) -> None:
    return FunctionNameImpl(mytimer)