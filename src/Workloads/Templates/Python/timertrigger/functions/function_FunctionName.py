import logging

import azure.functions as func

# In projects without blueprint discovery, import and register bp in function_app.py.
bp = func.Blueprint()

@bp.function_name(name="function_FunctionName")
@bp.timer_trigger(schedule="0 */5 * * * *", arg_name="timer", use_monitor=True)
def main(timer: func.TimerRequest) -> None:
    logging.info("Python timer trigger executed.")
