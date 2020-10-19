using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Functions.Cli.Kubernetes.KEDA.V2.Models;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.KEDA.V2
{
    public static class KedaV2Helper
    {
        internal static ScaledObjectV1Alpha1 GetScaledObject(string name, string @namespace, TriggersPayload triggers,
               DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas)
        {
            // TODO: Migrate to new version
            return new ScaledObjectV1Alpha1
            {
                ApiVersion = "keda.sh/v1alpha1",
                Kind = "ScaledObject",
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
                            Type = GetKedaTrigger(t["type"]?.ToString()),
                            Metadata = PopulateMetadataDictionary(t)
                        })
                }
            };
        }

        internal static IDictionary<string, string> PopulateMetadataDictionary(JToken t)
        {
            IDictionary<string, string> metadata = t.ToObject<Dictionary<string, JToken>>()
                .Where(i => i.Value.Type == JTokenType.String)
                .ToDictionary(k => k.Key, v => v.Value.ToString());

            // TODO: Check for breaking changes
            if (t["type"].ToString().Equals("rabbitMQTrigger", StringComparison.InvariantCultureIgnoreCase))
            {
                metadata["host"] = metadata["connectionStringSetting"];
                metadata.Remove("connectionStringSetting");
            }

            return metadata;
        }

        internal static string GetKedaTrigger(string triggerType)
        {
            if (string.IsNullOrEmpty(triggerType))
            {
                throw new ArgumentNullException(nameof(triggerType));
            }

            triggerType = triggerType.ToLower();

            // TODO: Check if we need to add new ones
            switch (triggerType)
            {
                case "queuetrigger":
                    return "azure-queue";

                case "kafkatrigger":
                    return "kafka";

                case "blobtrigger":
                    return "azure-blob";

                case "servicebustrigger":
                    return "azure-servicebus";

                case "eventhubtrigger":
                    return "azure-eventhub";

                case "rabbitmqtrigger":
                    return "rabbitmq";

                default:
                    return triggerType;
            }
        }
    }
}
