using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace AttributeMatchingTest
{
    using Microsoft.Azure.WebJobs; // need this here to find the extension methods

    public static partial class Function1
    {
        // Project for testing Durable type-safe helpers
        // Type tests cover verifying the types detected for generating code
        //  - Ensures the activity parameter is correctly identified (via that ActivityTrigger) 
        //    and is of the correct type
        //  - Ensures that the return type is generated correctly
        //     - Check converting non-async actions to Task/Task<T> return types
        //     - Check that async actions don't have an extra Task<T> wrapper
        //     - Check that simple types, qualified types, generic types are all handled

        [FunctionName("Function1")]
        public static async Task<int> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            // assign result to ensure return type is correct
            int i;
            i = await context.Activities().Test1Async(123);

            return 0;
        }
    }
}
namespace AttributeMatchingTest
{
    using Spoof;
    public static partial class Function1
    {
        // valid function - using qualifed attribute names
        [Microsoft.Azure.WebJobs.FunctionNameAttribute("Test1")]
        public static int Test1([Microsoft.Azure.WebJobs.ActivityTriggerAttribute] int name, ILogger log)
        {
            return 0;
        }
        // included "using Spoof" so unqualified references are spoofed
        // using overloads with different signatures, non of which use valid combinations of FunctionName + ActivityTrigger
        // if any of them are included then the generated code would have multiple methods with the same signature
        // so wouldn't compile
        [FunctionName("Test1")]
        public static string Test1([Microsoft.Azure.WebJobs.ActivityTrigger] int name, ILogger log, int i)
        {
            throw new Exception("Should not get here");
        }
        [Spoof.FunctionName("Test1")]
        public static string Test1([Microsoft.Azure.WebJobs.ActivityTrigger] int name, ILogger log, long i)
        {
            throw new Exception("Should not get here");
        }
        [Microsoft.Azure.WebJobs.FunctionName("Test1")]
        public static string Test1([ActivityTrigger] int name, ILogger log, string s, int i)
        {
            throw new Exception("Should not get here");
        }
        [Microsoft.Azure.WebJobs.FunctionName("Test1")]
        public static string Test1([Spoof.ActivityTrigger] int name, ILogger log, string s, long i)
        {
            throw new Exception("Should not get here");
        }

        [Microsoft.Azure.WebJobs.FunctionName("Function1_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [Microsoft.Azure.WebJobs.HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]HttpRequestMessage req,
            [Microsoft.Azure.WebJobs.OrchestrationClient]Microsoft.Azure.WebJobs.DurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("Function1", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
    namespace Spoof
    {
        public class FunctionNameAttribute : System.Attribute
        {
            public string Name { get; }
            public FunctionNameAttribute(string name)
            {
                Name = name;
            }
        }
        public class ActivityTriggerAttribute : System.Attribute
        {
        }
    }
}