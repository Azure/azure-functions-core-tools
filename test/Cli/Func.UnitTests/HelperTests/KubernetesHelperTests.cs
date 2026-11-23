// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.KEDA.V2.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    [Collection("NativeWorkerRuntimeTests")]
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
        [InlineData(@"{""status"":{""loadBalancer"":{""ingress"":[{},{""hostname"":""localhost""}]}}}", "localhost")]
        public async Task GetServiceIpReturnsIpOrHostname(string status, string expected)
        {
            var calls = 0;

            var result = await KubernetesHelper.GetServiceIp(
                NewService(),
                3,
                (_, _, _, _) =>
                {
                    calls++;
                    return Task.FromResult((LoadBalancerJson(status), true));
                },
                TimeSpan.Zero);

            result.Should().Be(expected);
            calls.Should().Be(1);
        }

        [Fact]
        public async Task GetServiceIpQueriesAgainUntilIngressHasAddress()
        {
            var responses = new Queue<string>(new[]
            {
                @"{""status"":{""loadBalancer"":{""ingress"":[{}]}}}",
                @"{""status"":{""loadBalancer"":{""ingress"":[{""hostname"":""localhost""}]}}}"
            });

            var calls = 0;

            var result = await KubernetesHelper.GetServiceIp(
                NewService(),
                3,
                (_, _, _, _) =>
                {
                    calls++;
                    return Task.FromResult((LoadBalancerJson(responses.Dequeue()), true));
                },
                TimeSpan.Zero);

            result.Should().Be("localhost");
            calls.Should().Be(2);
        }

        [Fact]
        public async Task GetServiceIpReturnsEmptyWhenIngressNeverHasAddress()
        {
            var result = await KubernetesHelper.GetServiceIp(
                NewService(),
                2,
                (_, _, _, _) => Task.FromResult((LoadBalancerJson(@"{""status"":{""loadBalancer"":{""ingress"":[{}]}}}"), true)),
                TimeSpan.Zero);

            result.Should().BeEmpty();
        }

        private static ServiceV1 NewService() => new ServiceV1 { Metadata = new ObjectMetadataV1 { Name = "svc", Namespace = "ns" } };

        private static string LoadBalancerJson(string status)
        {
            var json = JObject.Parse(status);
            json["metadata"] = new JObject { ["name"] = "svc", ["namespace"] = "ns" };
            json["spec"] = new JObject { ["type"] = "LoadBalancer" };
            return json.ToString();
        }
    }
}
