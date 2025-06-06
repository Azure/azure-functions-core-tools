﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.KEDA.V1.Models;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.KEDA.V1
{
    public class KedaV1Resource : KedaResourceBase
    {
        public override IKubernetesResource GetKubernetesResource(
            string name,
            string @namespace,
            TriggersPayload triggers,
            DeploymentV1Apps deployment,
            int? pollingInterval,
            int? cooldownPeriod,
            int? minReplicas,
            int? maxReplicas)
        {
            return new ScaledObjectKedaV1
            {
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace,
                    Labels = new Dictionary<string, string>
                    {
                        { "deploymentName", deployment.Metadata.Name }
                    }
                },
                Spec = new ScaledObjectSpecV1Alpha1
                {
                    ScaleTargetRef = new ScaledObjectScaleTargetRefV1Alpha1
                    {
                        DeploymentName = deployment.Metadata.Name
                    },
                    PollingInterval = pollingInterval,
                    CooldownPeriod = cooldownPeriod,
                    MinReplicaCount = minReplicas,
                    MaxReplicaCount = maxReplicas,
                    Triggers = triggers
                        .FunctionsJson
                        .Select(kv => kv.Value)
                        .Where(v => v["bindings"] != null)
                        .Select(b => b["bindings"])
                        .SelectMany(i => i)
                        .Where(b => b?["type"] != null)
                        .Where(b => b["type"].ToString().IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) != -1)
                        .Where(b => b["type"].ToString().IndexOf("httpTrigger", StringComparison.OrdinalIgnoreCase) == -1)
                        .Select(t => new ScaledObjectTriggerV1Alpha1
                        {
                            Type = GetKedaTriggerType(t["type"]?.ToString()),
                            Metadata = PopulateMetadataDictionary(t)
                        })
                }
            };
        }

        public override IDictionary<string, string> PopulateMetadataDictionary(JToken t)
        {
            IDictionary<string, string> metadata = t.ToObject<Dictionary<string, JToken>>()
                .Where(i => i.Value.Type == JTokenType.String)
                .ToDictionary(k => k.Key, v => v.Value.ToString());

            if (t["type"].ToString().Equals("rabbitMQTrigger", StringComparison.InvariantCultureIgnoreCase))
            {
                metadata["host"] = metadata["connectionStringSetting"];
                metadata.Remove("connectionStringSetting");
            }

            return metadata;
        }
    }
}
