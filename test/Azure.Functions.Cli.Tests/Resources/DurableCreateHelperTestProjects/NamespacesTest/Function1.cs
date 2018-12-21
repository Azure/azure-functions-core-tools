using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace NamespacesTest
{
    namespace Nested1
    {
        public class SomeType
        {
            public int Property { get; set; }
        }
    }
    namespace Nested2
    {
        public class SomeType
        {
            public int Property { get; set; }
        }
    }

    public static partial class Function1
    {
        // Project for testing namespace qualification
        // Namespace tests cover verifying the qualification of types in generated code
        //  - Ensures that types are qualified e.g. to handle typename clashes across namespaces

        [FunctionName("Function1")]
        public static async Task<int> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            // assign result to ensure return type is correct
            Nested1.SomeType nst1 = await context.Activities().Test1Async(new Nested1.SomeType { Property = 123 });
            Nested2.SomeType nst2 = await context.Activities().Test2Async(new Nested2.SomeType { Property = 123 });

            return 0;
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
namespace NamespacesTest
{
    using Nested1;
    public static partial class Function1
    {
        [FunctionName("Test1")]
        public static SomeType Test1([ActivityTrigger] SomeType s)
        {
            return s;
        }
    }
}
namespace NamespacesTest
{
    using Nested2;
    public static partial class Function1
    {
        [FunctionName("Test2")]
        public static SomeType Test2([ActivityTrigger] SomeType s)
        {
            return s;
        }
    }
}