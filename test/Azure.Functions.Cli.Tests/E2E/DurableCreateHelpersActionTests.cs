using Azure.Functions.Cli.Actions.DurableActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class DurableCreateHelpersActionTests : BaseE2ETest
    {
        private static readonly string StorageConnectionString = Environment.GetEnvironmentVariable("DURABLE_STORAGE_CONNECTION");

        private static readonly string WorkingDirBasePath = Path.Combine(Environment.GetEnvironmentVariable("DURABLE_FUNCTION_PATH"), "..\\DurableCreateHelperTestProjects");

        private const string _storageReason = "Durable E2E tests need a storage account connection";

        public DurableCreateHelpersActionTests(ITestOutputHelper output) : base(output)
        {
        }

        [SkippableFact]
        public async Task basic_call_activity_test()
        {
            await RunResourceTest(
                taskHubName: "basicTest",
                testFolderName: "BasicCallActivityTest",
                resultVerifier: result =>
                {
                    Assert.NotNull(result);
                    var runtimeStatus = (string)result.runtimeStatus;
                    runtimeStatus.Should().Be("Completed", because: "the orchestration should complete successfully");

                    var output = ((JArray)result.output).ToObject<string[]>();
                    output.Should().BeEquivalentTo("Hello Tokyo!", "Hello Seattle!", "Hello London!");
                });
        }

        [SkippableFact]
        public async Task types_test()
        {
            await RunResourceTest(
                taskHubName: "typesTest",
                testFolderName: "TypesTest",
                resultVerifier: result =>
                {
                    Assert.NotNull(result);
                    var output = (int)result.output;
                    output.Should().Be(0);
                });
        }

        [SkippableFact]
        public async Task attribute_matching_test()
        {
            await RunResourceTest(
                taskHubName: "attributeTest",
                testFolderName: "AttributeMatchingTest",
                resultVerifier: result =>
                {
                    Assert.NotNull(result);
                    var output = (int)result.output;
                    output.Should().Be(0);
                });
        }

        [SkippableFact]
        public async Task namespaces_test()
        {
            await RunResourceTest(
                taskHubName: "namespaceTest",
                testFolderName: "NamespacesTest",
                resultVerifier: result =>
                {
                    Assert.NotNull(result);
                    var output = (int)result.output;
                    output.Should().Be(0);
                });
        }

        private async Task RunResourceTest(
                string taskHubName, 
                string testFolderName, 
                Action<dynamic> resultVerifier)
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);


            var workingDirPath = Path.Combine(WorkingDirBasePath, testFolderName);

            DurableHelper.SetTaskHubName(workingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            DeleteFileIfExists(Path.Combine(workingDirPath, DurableCreateHelpersAction.GeneratedFileName));

            await CliTester.Run(new RunConfiguration
            {
                Commands = new string[]
                {
                    "settings decrypt",
                    "settings add FUNCTIONS_WORKER_RUNTIME dotnet",
                    "durable create-helpers",
                    "start --build --port 7073"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        var statusUri = await StartNewOrchestrationAsync(client, "/api/Function1_HttpStart");
                        dynamic result = await WaitForCompletionAsync(
                            client,
                            statusUri,
                            pollInterval: TimeSpan.FromSeconds(5),
                            completionTimeout: TimeSpan.FromSeconds(30));

                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));


                        resultVerifier(result);
                    }
                },
            }
            , _output
            , workingDir: workingDirPath
            );

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }



        private void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static async Task<string> StartNewOrchestrationAsync(HttpClient client, string orchestrationStartUri)
        {
            var response = await client.GetAsync(orchestrationStartUri);
            dynamic result = await response.Content.ReadAsAsync<dynamic>();

            var statusUri = (string)result.statusQueryGetUri;
            return statusUri;
        }
        private static async Task<dynamic> WaitForCompletionAsync(HttpClient client, string statusUri, TimeSpan pollInterval, TimeSpan completionTimeout)
        {
            var cts = new CancellationTokenSource(completionTimeout);
            var cancellationToken = cts.Token;

            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await client.GetAsync(statusUri, cancellationToken);
                dynamic result = await response.Content.ReadAsAsync<dynamic>();
                if (result.runtimeStatus != "Running")
                {
                    return result;
                }
                await Task.Delay(pollInterval, cancellationToken);
            }
            return null;
        }
    }
}
