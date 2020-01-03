using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
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

        public static RunConfiguration GenerateCheckFunctionTask(string appName, string triggerName)
        {
            return new RunConfiguration
            {
                Commands = new[]
                {
                    $"azure functionapp list-functions {appName}"
                },
                OutputContains = new string[] { $"    HttpTrigger - [{triggerName}]" },
                CommandTimeout = TimeSpan.FromMinutes(3)
            };
        }

        public static string GetRandomTriggerName()
        {
            return Guid.NewGuid().ToString().Split('-')[0];
        }
    }
}
