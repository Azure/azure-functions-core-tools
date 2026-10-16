@app.dapr_service_invocation_trigger(arg_name="payload", method_name="__FUNCTION_NAME__") 
def __FUNCTION_NAME__(payload: str) :
    """
    See https://aka.ms/azure-functions-dapr for more information about using this binding
    
    These tasks should be completed prior to running :
         1. Install Dapr
    Run the app with below steps
         1. Start function app with Dapr: dapr run --app-id functionapp --app-port 3001 --dapr-http-port 3501 -- func host start
         2. Invoke function app by dapr cli: dapr invoke --app-id functionapp --method {yourFunctionName}  --data '{ "data": {"value": { "orderId": "41" } } }'
    """
    logging.info('Azure function triggered by Dapr Service Invocation Trigger.')
    logging.info("Dapr service invocation trigger payload: %s", payload)

@app.function_name(name="InvokeOutputBinding")
@app.route(route="invoke/{appId}/{methodName}", auth_level=app.auth_level.ANONYMOUS)
@app.dapr_invoke_output(arg_name = "payload", app_id = "{appId}", method_name = "{methodName}", http_verb = "post")
def main(req: func.HttpRequest, payload: func.Out[str] ) -> str:
    """
    Sample to use a Dapr Invoke Output Binding to perform a Dapr Server Invocation operation hosted in another Darp'd app.
    Here this function acts like a proxy
    Invoke Dapr Service invocation trigger using Windows PowerShell with below request

    Invoke-RestMethod -Uri 'http://localhost:7071/api/invoke/functionapp/{yourFunctionName}' -Method POST -Headers @{
    'Content-Type' = 'application/json'
     } -Body '{
     "data": {
          "value": {
               "orderId": "122"
               }
          }
     }'
    """
    logging.info('Python HTTP trigger function processed a request..')
    logging.info(req.params)
    data = req.params.get('data')
    if not data:
        try:
            req_body = req.get_json()
        except ValueError:
            pass
        else:
            data = req_body.get('data')

    if data:
        logging.info(f"Url: {req.url}, Data: {data}")
        payload.set(json.dumps({"body": data}).encode('utf-8'))
        return 'Successfully performed service invocation using Dapr invoke output binding.'
    else:
        return func.HttpResponse(
            "Please pass a data on the query string or in the request body",
            status_code=400
        )