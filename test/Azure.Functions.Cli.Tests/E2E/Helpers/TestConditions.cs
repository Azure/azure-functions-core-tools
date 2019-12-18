using System;
using Xunit;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public static class TestConditions
    {
        public static void SkipIfAzureServicePrincipalNotDefined()
        {
            var directoryId = Environment.GetEnvironmentVariable("AZURE_DIRECTORY_ID");
            var appId = Environment.GetEnvironmentVariable("AZURE_SERVICE_PRINCIPAL_ID");
            var key = Environment.GetEnvironmentVariable("AZURE_SERVICE_PRINCIPAL_KEY");

            Skip.If(string.IsNullOrEmpty(directoryId) ||
                    string.IsNullOrEmpty(appId) ||
                    string.IsNullOrEmpty(key),
                reason: "One or more of AZURE_DIRECTORY_ID, AZURE_SERVICE_PRINCIPAL_ID, AZURE_SERVICE_PRINCIPAL_KEY is not defined");
        }

        public static void SkipIfEnableDeploymentTestsNotDefined()
        {
            Skip.If(!EnvironmentHelper.GetEnvironmentVariableAsBool(E2ETestConstants.EnableDeploymentTests),
                reason: $"{E2ETestConstants.EnableDeploymentTests} is not set to true");
        }
    }
}