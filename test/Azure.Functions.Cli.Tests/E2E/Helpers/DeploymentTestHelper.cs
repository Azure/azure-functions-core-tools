using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Helpers;
using DryIoc;
using FluentAssertions;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public static class DeploymentTestHelper
    {
        public static RunConfiguration GenerateInitTask(string triggerName)
        {
            return new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime python",
                    $"new -l python -t HttpTrigger -n {triggerName}",
                },
                Test = async (workingDir, _) =>
                {
                    var filePath = Path.Combine(workingDir, triggerName, "function.json");
                    var functionJson = await File.ReadAllTextAsync(filePath);
                    functionJson = functionJson.Replace("\"authLevel\": \"function\"", $"\"authLevel\": \"anonymous\"");
                    await File.WriteAllTextAsync(filePath, functionJson);
                }
            };
        }

        public static RunConfiguration GeneratePublishTask(
            string appName,
            string publishFlags = null,
            string[] expectedOutput = null)
        {
            return new RunConfiguration
            {
                Commands = new[]
                {
                    $"azure functionapp publish {appName} {publishFlags ?? string.Empty}"
                },
                OutputContains = expectedOutput,
                CommandTimeout = TimeSpan.FromMinutes(3)
            };
        }

        public static RunConfiguration GenerateRequestTask(string appName, string triggerName, int retries = 5, int intervalSec = 10)
        {
            return new RunConfiguration
            {
                Test = async (workingDir, _) =>
                {
                    await RetryHelper.Retry(async () =>
                    {
                        string url = $"https://{appName}.azurewebsites.net/";
                        using (var client = new HttpClient() { BaseAddress = new Uri(url) })
                        {
                            var response = await client.GetAsync($"/api/{triggerName}?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            var trimmedResult = result.Trim(new[] { '!', '.' });
                            trimmedResult.Should().Be(
                                expected: "Hello Test",
                                because: $"trigger {triggerName} should respond 'Hello Test'");
                        }
                    }, retryCount: retries, retryDelay: TimeSpan.FromSeconds(intervalSec));
                }
            };
        }
    }
}
