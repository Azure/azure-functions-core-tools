import logging
import json

import azure.functions as func
import azure.durable_functions as df

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)

#Entity Function
@app.entity_trigger(context_name="context")
def __FUNCTION_NAME__(context: df.DurableEntityContext):
    """A Counter Durable Entity.
    A simple example of a Durable Entity that implements
    a simple counter.
    Parameters
    ----------
    context (df.DurableEntityContext):
        The Durable Entity context, which exports an API
        for implementing durable entities.
    """

    current_value = context.get_state(lambda: 0)
    operation = context.operation_name
    if operation == "add":
        amount = context.get_input()
        current_value += amount
    elif operation == "reset":
        current_value = 0
    elif operation == "get":
        pass

    context.set_state(current_value)
    context.set_result(current_value)