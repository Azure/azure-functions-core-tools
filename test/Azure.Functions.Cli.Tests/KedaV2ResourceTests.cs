using System.Collections.Generic;
using System.Linq;
using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.KEDA.V2;
using Azure.Functions.Cli.Kubernetes.KEDA.V2.Models;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class KedaV2ResourceTests
    {
        private readonly Dictionary<string, JObject> _functions = new Dictionary<string, JObject>();

        public KedaV2ResourceTests()
        {
            _functions.Add(
                "HelloOrchestration",
                JObject.Parse(@"
{
  ""generatedBy"": ""Microsoft.NET.Sdk.Functions-3.0.13"",
  ""configurationSource"": ""attributes"",
  ""bindings"": [
    {
      ""type"": ""orchestrationTrigger"",
      ""name"": ""context""
    }
  ],
  ""disabled"": false,
  ""scriptFile"": ""../bin/Functions.HelloWorld.dll"",
  ""entryPoint"": ""Functions.HelloWorld.HelloOrchestration""
}
"));
        }

        [Theory]
        [InlineData("{ }")]
        [InlineData("{ \"extensions\": null }")]
        [InlineData("{ \"extensions\": { } }")]
        [InlineData("{ \"extensions\": { \"durableTask\": null } }")]
        public void GetDurableScalarNoExtension(string hostSnippet)
        {
            JObject hostConfig = JObject.Parse(hostSnippet);
            ScaledObjectKedaV2 scaledObject = GetKubernetesResource(hostConfig);

            Assert.NotNull(scaledObject);
            Assert.Empty(scaledObject.Spec.Triggers);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("{ }")]
        [InlineData("{ \"type\": \"vnext\" }")]
        public void GetUnsupportedDurableScalar(string providerJson)
        {
            string hostSnippet = @"
{
  ""extensions"": {
    ""maxConcurrentOrchestratorFunctions"": 5,
    ""maxConcurrentActivityFunctions"": 10,
    ""durableTask"": {
    }
  }
}
";

            JObject hostConfig = JObject.Parse(hostSnippet);
            JObject durableTaskConfig = hostConfig.SelectToken("extensions.durableTask") as JObject;

            if (providerJson != null)
                durableTaskConfig.Add("storageProvider", JObject.Parse(providerJson));

            ScaledObjectKedaV2 scaledObject = GetKubernetesResource(hostConfig);

            Assert.NotNull(scaledObject);
            Assert.Empty(scaledObject.Spec.Triggers);
        }

        [Theory]
        [InlineData(null, null, 10, 1)]
        [InlineData(3, 6, 3, 6)]
        public void GetMsSqlDurableScalar(
            int? configuredMaxOrchestrations,
            int? configuredMaxActivities,
            int expectedMaxOrchestrations,
            int expectedMaxActivities)
        {
            string hostSnippet = @"
{
  ""extensions"": {
    ""durableTask"": {
      ""storageProvider"": {
        ""type"": ""mssql"",
        ""connectionStringName"": ""MySqlConnection"",
        ""taskEventLockTimeout"": ""00:01:00"",
        ""partitionCount"": 5
      }
    }
  }
}
";

            JObject hostConfig = JObject.Parse(hostSnippet);
            JObject durableTaskConfig = hostConfig.SelectToken("extensions.durableTask") as JObject;

            if (configuredMaxOrchestrations.HasValue)
                durableTaskConfig.Add("maxConcurrentOrchestratorFunctions", configuredMaxOrchestrations);

            if (configuredMaxActivities.HasValue)
                durableTaskConfig.Add("maxConcurrentActivityFunctions", configuredMaxActivities);

            ScaledObjectKedaV2 scaledObject = GetKubernetesResource(hostConfig);

            Assert.NotNull(scaledObject);
            AssertMsSqlDurableScalar(scaledObject.Spec.Triggers.Single(), expectedMaxOrchestrations, expectedMaxActivities, "MySqlConnection");
        }

        private ScaledObjectKedaV2 GetKubernetesResource(JObject hostJson)
        {
            KedaV2Resource resource = new KedaV2Resource();
            return resource.GetKubernetesResource(
                "HelloWorld",
                "default",
                new TriggersPayload { HostJson = hostJson, FunctionsJson = _functions },
                new DeploymentV1Apps { Metadata = new ObjectMetadataV1 { Name = "HelloDeployment" } },
                30,
                300,
                1,
                8) as ScaledObjectKedaV2;
        }

        private static void AssertMsSqlDurableScalar(ScaledObjectTriggerV1Alpha1 actual, int maxOrchestrations, int maxActivities, string connectionString)
        {
            Assert.NotNull(actual);
            Assert.Equal("mssql", actual.Type);
            Assert.Equal(3, actual.Metadata.Count);
            Assert.Equal($"SELECT dt.GetScaleRecommendation({maxOrchestrations}, {maxActivities})", actual.Metadata["query"]);
            Assert.Equal("1", actual.Metadata["targetValue"]);
            Assert.Equal(connectionString, actual.Metadata["connectionStringFromEnv"]);
        }
    }
}
