// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.KEDA.V2.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class KubernetesHelperTests
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
        public void ValidateYamlStringQuote()
        {
            var result = KubernetesHelper.SerializeResources(
            [
                new ScaledObjectKedaV2
                {
                    Spec = new ScaledObjectSpecV1Alpha1
                    {
                        Triggers =
                        [
                            new ScaledObjectTriggerV1Alpha1
                            {
                                Metadata = new Dictionary<string, string>
                                {
                                    ["targetValue"] = "1",
                                }
                            }
                        ]
                    }
                }
            ],
            Kubernetes.Models.OutputSerializationOptions.Yaml);

            result.Should().Contain("\"1\"");
        }

        [Theory]
        [InlineData(@"{""status"":{""loadBalancer"":{""ingress"":[{""ip"":""192.0.2.1""}]}}}", "192.0.2.1")]
        [InlineData(@"{""status"":{""loadBalancer"":{""ingress"":[{""hostname"":""localhost""}]}}}", "localhost")]
        [InlineData(@"{""status"":{""loadBalancer"":{""ingress"":[{""ip"":""192.0.2.1"",""hostname"":""example.test""}]}}}", "192.0.2.1")]
        [InlineData(@"{""status"":{""loadBalancer"":{""ingress"":[]}}}", null)]
        public void GetServiceAddressReturnsIpOrHostname(string json, string expected)
        {
            var service = JsonConvert.DeserializeObject<ServiceV1>(json);

            KubernetesHelper.GetServiceAddress(service).Should().Be(expected);
        }
    }
}
