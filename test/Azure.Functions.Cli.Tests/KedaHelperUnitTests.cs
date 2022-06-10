using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Azure.Functions.Cli.Kubernetes.KEDA.V1;
using Azure.Functions.Cli.Kubernetes.KEDA.V2;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class KedaHelperUnitTests
    {
        [Fact]
        public void PopulateMetadataDictionary_KedaV1_CorrectlyPopulatesRabbitMQMetadata()
        {
            string jsonText = @"
            {
                ""type"": ""rabbitMQTrigger"",
                ""connectionStringSetting"": ""RabbitMQConnection"",
                ""queueName"": ""myQueue"",
                ""name"": ""message""
            }";

            JToken jsonObj = JToken.Parse(jsonText);

            IDictionary<string, string> metadata = new KedaV1Resource().PopulateMetadataDictionary(jsonObj);

            Assert.Equal(4, metadata.Count);
            Assert.True(metadata.ContainsKey("type"));
            Assert.True(metadata.ContainsKey("host"));
            Assert.True(metadata.ContainsKey("name"));
            Assert.True(metadata.ContainsKey("queueName"));
            Assert.Equal("rabbitMQTrigger", metadata["type"]);
            Assert.Equal("RabbitMQConnection", metadata["host"]);
            Assert.Equal("message", metadata["name"]);
            Assert.Equal("myQueue", metadata["queueName"]);
        }

        [Fact]
        public void PopulateMetadataDictionary_KedaV2_CorrectlyPopulatesRabbitMQMetadata()
        {
            string jsonText = @"
            {
                ""type"": ""rabbitMQTrigger"",
                ""connectionStringSetting"": ""RabbitMQConnection"",
                ""queueName"": ""myQueue"",
                ""name"": ""message""
            }";

            JToken jsonObj = JToken.Parse(jsonText);

            IDictionary<string, string> metadata = new KedaV2Resource().PopulateMetadataDictionary(jsonObj);

            Assert.Equal(2, metadata.Count);
            Assert.True(metadata.ContainsKey("queueName"));
            Assert.True(metadata.ContainsKey("hostFromEnv"));
            Assert.Equal("myQueue", metadata["queueName"]);
            Assert.Equal("RabbitMQConnection", metadata["hostFromEnv"]);
        }

        [Fact]
        public void PopulateMetadataDictionary_KedaV2_CorrectlyPopulatesAzureStorageQueueMetadata()
        {
            string jsonText = @"
            {
                ""type"": ""queuetrigger"",
                ""connection"": ""StorageQueueConnection"",
                ""queueName"": ""myQueue"",
                ""name"": ""context""
            }";

            JToken jsonObj = JToken.Parse(jsonText);

            IDictionary<string, string> metadata = new KedaV2Resource().PopulateMetadataDictionary(jsonObj);

            Assert.Equal(2, metadata.Count);
            Assert.True(metadata.ContainsKey("queueName"));
            Assert.True(metadata.ContainsKey("connectionFromEnv"));
            Assert.Equal("myQueue", metadata["queueName"]);
            Assert.Equal("StorageQueueConnection", metadata["connectionFromEnv"]);
        }

        [Fact]
        public void PopulateMetadataDictionary_KedaV2_CorrectlyPopulatesAzureStorageQueueMetadata_WithoutConnection()
        {
            string jsonText = @"
            {
                ""type"": ""queuetrigger"",
                ""queueName"": ""myQueue"",
                ""name"": ""context""
            }";

            JToken jsonObj = JToken.Parse(jsonText);

            IDictionary<string, string> metadata = new KedaV2Resource().PopulateMetadataDictionary(jsonObj);

            Assert.Equal(2, metadata.Count);
            Assert.True(metadata.ContainsKey("queueName"));
            Assert.True(metadata.ContainsKey("connectionFromEnv"));
            Assert.Equal("myQueue", metadata["queueName"]);
            Assert.Equal("AzureWebJobsStorage", metadata["connectionFromEnv"]);
        }
    }
}