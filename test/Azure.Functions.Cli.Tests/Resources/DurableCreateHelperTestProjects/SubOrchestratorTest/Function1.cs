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
        // SubOrchestrator tests cover ??????????????????????????????????????????????????????????????


        [FunctionName("Parent")]
        public static async Task<string> ParentOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var name = context.GetInput<string>();
            var retryOptions = new RetryOptions(TimeSpan.FromSeconds(10), 2);

            // non-retry call to orchestrator that returns a value
            var result = await context.SubOrchestrators().ChildOrchestratorAsync(name);
            // retry call to orchestrator that returns a value
            result = await context.SubOrchestrators().ChildOrchestratorWithRetryAsync(retryOptions, name);

            // non-retry call to orchestrator that doesn't return a value
            await context.SubOrchestrators().VoidOrchestratorAsync("void");
            // retry call to orchestrator that returns a value
            await context.SubOrchestrators().VoidOrchestratorWithRetryAsync(retryOptions, "void");

            return result;
        }

        [FunctionName("Child")]
        public static async Task<string> ChildOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var input = context.GetInput<string>();
            var result = await context.Activities().SayHelloAsync(input);

            return result;
        }
        [FunctionName("Void")]
        public static async Task VoidOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var input = context.GetInput<string>();
            var result = await context.Activities().SayHelloAsync(input);

            return;
        }

        [FunctionName("Function1_Hello")]
        public static string SayHello([ActivityTrigger] string name, ILogger log) // Standard activity call
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }

        [FunctionName("Parent_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("Parent", "Test");

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}