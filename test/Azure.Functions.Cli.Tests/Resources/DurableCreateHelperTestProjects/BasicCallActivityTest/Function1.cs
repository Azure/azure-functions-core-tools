using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace BaseCallActivityTest
{
    public static class Function1
    {
        // Project for testing Durable type-safe helpers
        // Basic tests cover calling CallXXXAsync, CallXXXWithRetryAsync generated methods
        //   - Ensures helpers exist
        //   - Ensures helpers use the method name as the helper but the function name 
        //     (from the FunctionName attribute) when invoking CallActivitysync
        //   - Helper names include 'Async' suffix if the method name doesn't already

        [FunctionName("Function1")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            // Replace "hello" with the name of your Durable Activity Function.
            outputs.Add(await context.Activities().SayHelloAsync("Tokyo"));
            outputs.Add(await context.Activities().SayHello2WithRetryAsync(new RetryOptions(TimeSpan.FromSeconds(5), 2),  "Seattle"));
            outputs.Add(await context.Activities().SayHello3Async("London"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return outputs;
        }

        [FunctionName("Function1_Hello")]
        public static string SayHello([ActivityTrigger] string name, ILogger log) // Standard activity call
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }
        [FunctionName("Function1_Hello2")]
        public static string SayHello2(ILogger log, [ActivityTrigger] string name) // swap activity trigger argument position
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }
        [FunctionName("Function1_Hello3")]
        public static async Task<string> SayHello3Async([ActivityTrigger] string name, ILogger log) // Async call with Task<T> return type
        {
            await Task.Delay(100);
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }

        [FunctionName("Function1_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("Function1", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}