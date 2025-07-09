// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.KEDA.V2.Models;
using FluentAssertions;
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
    }
}
