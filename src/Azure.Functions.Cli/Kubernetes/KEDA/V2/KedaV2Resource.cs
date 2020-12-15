using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.KEDA.V2.Models;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;

namespace Azure.Functions.Cli.Kubernetes.KEDA.V2
{
    public class KedaV2Resource : KedaResourceBase
    {
        public override IKubernetesResource GetKubernetesResource(string name, string @namespace, TriggersPayload triggers,
               DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas)
        {
            return new ScaledObjectKedaV2
            {
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace,
                    Labels = new Dictionary<string, string>()
                },
                Spec = new ScaledObjectSpecV1Alpha1
                {
                    ScaleTargetRef = new ScaledObjectScaleTargetRefV1Alpha1
                    {
                        Name = deployment.Metadata.Name
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
    }
}