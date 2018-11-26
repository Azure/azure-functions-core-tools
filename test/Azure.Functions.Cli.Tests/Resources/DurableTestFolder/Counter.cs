using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace durablefx
{
    public static class Counter
    {
        [FunctionName("HttpStartSingle")]
        public static async Task<HttpResponseMessage> HttpStartSingle(
            [HttpTrigger(AuthorizationLevel.Anonymous, methods: "post", Route = "orchestrators/{functionName}/{instanceId}")] HttpRequestMessage req,
            [OrchestrationClient] DurableOrchestrationClient starter,
            string functionName,
            string instanceId,
            ILogger log)
        {
            // Check if an instance with the specified ID already exists.
            if (starter != null)
            {
                var existingInstance = await starter.GetStatusAsync(instanceId, showHistory: false, showHistoryOutput: false);

                if (existingInstance == null)
                {
                    // An instance with the specified ID doesn't exist, create one.
                    dynamic eventData = await req.Content.ReadAsAsync<object>();
                    await starter.StartNewAsync(functionName, instanceId, eventData);
                    log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
                    return starter.CreateCheckStatusResponse(req, instanceId);
                }
                else
                {
                    // An instance with the specified ID exists, don't create one.
                    return req.CreateErrorResponse(
                        HttpStatusCode.Conflict,
                        $"An instance with ID '{instanceId}' already exists. Status: {existingInstance.RuntimeStatus.ToString()}");
                }
            }
            else
            {
                return req.CreateErrorResponse(
                    HttpStatusCode.BadRequest,
                    $"DurableOrchestrationClient parameter from Durable Functions binding could not be resolved.");
            }
            

        }

        [FunctionName("Counter")]
        public static async Task Math([OrchestrationTrigger] DurableOrchestrationContext ctx)
        {
            int counterState = ctx.GetInput<int>();
            string operation = await ctx.WaitForExternalEvent<string>("operation");

            if (operation.Equals("add"))
            {
                counterState++;
            }
            else if (operation.Equals("subtract"))
            {
                counterState--;
            }
            else
            {
                throw new ArgumentException("Event data should be either 'add' or 'subtract'");            
            }

            ctx.ContinueAsNew(counterState);
        }

        [FunctionName("JsonInput")]
        public static async Task JsonInput([OrchestrationTrigger] DurableOrchestrationContext ctx)
        {
            var input = ctx.GetInput<dynamic>();

            if (input != null)
            {
                string helloTest = input.Hello;
            }

            var data = await ctx.WaitForExternalEvent<dynamic>("parse");

            string nameTest = data.Name;

            ctx.ContinueAsNew(data);
        }
    }
}
