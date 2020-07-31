using Azure.Functions.Cli.Kubernetes;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class KubernetesHelperUnitTests
    {
        [Theory]
        [InlineData("normalname", true)]
        [InlineData("name-with-dashes", true)]
        [InlineData("name-with-numbers-938-234", true)]
        [InlineData("name with spaces", false)]
        [InlineData("NameWithCapital", false)]
        [InlineData("name@something", false)]
        public void ValidateKubernetesNames(string name, bool isValid)
        {
            try
            {
                KubernetesHelper.ValidateKubernetesName(name);
            }
            catch
            {
                if (isValid)
                {
                    throw;
                }
            }
        }

        [Fact]
        public void PopulateMetadataDictionary_CorrectlyPopulatesRabbitMQMetadata()
        {
            string jsonText = @"
            {
                ""type"": ""rabbitMQTrigger"",
                ""connectionStringSetting"": ""RabbitMQConnection"",
                ""queueName"": ""myQueue"",
                ""name"": ""message""
            }";

            JToken jsonObj = JToken.Parse(jsonText);

            IDictionary<string, string> metadata = KubernetesHelper.PopulateMetadataDictionary(jsonObj);

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
    }
}