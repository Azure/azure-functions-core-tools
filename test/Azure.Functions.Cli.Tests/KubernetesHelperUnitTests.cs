using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Kubernetes;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.ContentModel;
using System;
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

        [Fact]
        public void GetServiceAccountTest()
        {
            var svcActName = "function-identity-svc-act";
            var @namespace = "funcappkeys-test-ns0";
            var resource = KubernetesHelper.GetServiceAccount(svcActName, @namespace);
            var payload = JsonConvert.SerializeObject(resource, Formatting.Indented,
               new JsonSerializerSettings
               {
                   NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
               });

            Assert.True(JToken.Parse(payload) is JToken);
        }

        [Fact]
        public void GetRoleTest()
        {
            var roleName = "secrets-manager-role";
            var @namespace = "funcappkeys-test-ns0";
            var resource = KubernetesHelper.GetRole(roleName, @namespace);
            var payload = JsonConvert.SerializeObject(resource, Formatting.Indented,
               new JsonSerializerSettings
               {
                   NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
               });

            Assert.True(JToken.Parse(payload) is JToken);
        }

        [Fact]
        public void GetRoleBindingTest()
        {
            var roleBindingName = "function-identity-svcact-to-secret-manager-rolebinding";
            var roleName = "secrets-manager-role";
            var svcActName = "function-identity-svc-act";
            var @namespace = "funcappkeys-test-ns0";
            var resource = KubernetesHelper.GetRoleBinding(roleBindingName, @namespace, roleName, svcActName);
            var payload = JsonConvert.SerializeObject(resource, Formatting.Indented,
               new JsonSerializerSettings
               {
                   NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
               });

            Assert.True(JToken.Parse(payload) is JToken);
        }
    }
}