using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace TypesTest
{
    public static class Function1
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
            i = await context.Activities().Test2Async(123);
            i = await context.Activities().Test3Async(123);

            SomeType st = await context.Activities().Test4Async(new SomeType { Property = 123 });
            Nested.SomeNestedType nst = await context.Activities().Test5Async(new Nested.SomeNestedType { Property = 123 });

            IEnumerable<int> enumerable;
            enumerable = await context.Activities().Test6Async(new[] { 1 });
            enumerable = await context.Activities().Test7Async(new[] { 1 });

            await context.Activities().Test8Async(123);
            await context.Activities().Test9Async(123);

            return 0;
        }

        // non-async, int parameter, int return type
        [FunctionName("Test1")]
        public static int Test1([ActivityTrigger] int name, ILogger log)
        {
            return 0;
        }
        // async, int parameter, Task<int> return type, activity parameter not first
        [FunctionName("Test2")]
        public static Task<int> Test2Async(ILogger log, [ActivityTrigger] int name)
        {
            return Task.FromResult(0);
        }
        // non-async, Int32 parameter, System.Int32 return type - non primitive types with and without qualifiers
        [FunctionName("Test3")]
        public static System.Int32 Test3([ActivityTrigger] Int32 name, ILogger log)
        {
            return 0;
        }

        // non-async, custom type parameter, custom type return type
        [FunctionName("Test4")]
        public static SomeType Test4([ActivityTrigger] SomeType name, ILogger log)
        {
            return name;
        }
        // non-async, custom type parameter, custom type return type
        [FunctionName("Test5")]
        public static Nested.SomeNestedType Test5([ActivityTrigger] Nested.SomeNestedType name, ILogger log)
        {
            return name;
        }

        // non-async, generic parameter, generic return type
        [FunctionName("Test6")]
        public static IEnumerable<int> Test6([ActivityTrigger] IEnumerable<int> name, ILogger log)
        {
            return name;
        }
        // async, qualified generic parameter, qualified generic return type
        [FunctionName("Test7")]
        public static Task<System.Collections.Generic.IEnumerable<int>> Test7([ActivityTrigger] System.Collections.Generic.IEnumerable<int> name, ILogger log)
        {
            return Task.FromResult(name);
        }

        // non-async, void
        [FunctionName("Test8")]
        public static void Test8([ActivityTrigger] int name, ILogger log)
        {
            return;
        }
        // async, Task
        [FunctionName("Test9")]
        public static Task Test9([ActivityTrigger] int name, ILogger log)
        {
            return Task.CompletedTask;
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

        public class SomeType
        {
           public int Property { get; set; }
        }

    }
    namespace Nested
    {
        // TODO - add a test for a two classes with the same name in different namespaces

        public class SomeNestedType
        {
            public int Property { get; set; }
        }
    }
}