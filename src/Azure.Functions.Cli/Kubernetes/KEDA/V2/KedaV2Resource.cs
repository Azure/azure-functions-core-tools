using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.KEDA.V2.Models;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;

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
                        .GroupBy(b => IsDurable(b)) // Multiple durable triggers map to a single scaler
                        .SelectMany(group => group.Key ? GetDurableScalar(triggers.HostJson) : group.Select(GetStandardScalar))
                }
            };
        }

        private static bool IsDurable(JToken trigger) =>
            trigger["type"].ToString().Equals("orchestrationTrigger", StringComparison.OrdinalIgnoreCase) ||
            trigger["type"].ToString().Equals("activityTrigger", StringComparison.OrdinalIgnoreCase) ||
            trigger["type"].ToString().Equals("entityTrigger", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<ScaledObjectTriggerV1Alpha1> GetDurableScalar(JObject hostJson)
        {
            // Reference: https://docs.microsoft.com/azure/azure-functions/durable/durable-functions-bindings#durable-functions-2-0-host-json
            DurableTaskConfig durableTaskConfig = hostJson.SelectToken("extensions.durableTask")?.ToObject<DurableTaskConfig>();
            string storageType = durableTaskConfig?.StorageProvider?["type"]?.ToString();

            // Custom storage types are supported starting in Durable Functions v2.4.2
            if (string.Equals(storageType, "MicrosoftSQL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(storageType, "mssql", StringComparison.OrdinalIgnoreCase))
            {
                // By default, max 10 orchestrations and 1 activity per replica
                string query = string.Format(
                    CultureInfo.InvariantCulture,
                    "SELECT dt.GetScaleRecommendation({0}, {1})",
                    durableTaskConfig.MaxConcurrentOrchestratorFunctions,
                    durableTaskConfig.MaxConcurrentActivityFunctions);

                yield return new ScaledObjectTriggerV1Alpha1
                {
                    // MSSQL scaler reference: https://keda.sh/docs/2.2/scalers/mssql/
                    Type = "mssql",
                    Metadata = new Dictionary<string, string>
                    {
                        // Durable SQL scaling: https://microsoft.github.io/durabletask-mssql/#/scaling?id=worker-auto-scale
                        ["query"] = query,
                        ["targetValue"] = "1",
                        ["connectionStringFromEnv"] = durableTaskConfig.StorageProvider["connectionStringName"]?.ToString(),
                    }
                };
            }
            else
            {
                // TODO: Support for the Azure Storage and Netherite backends
            }
        }

        private ScaledObjectTriggerV1Alpha1 GetStandardScalar(JToken binding)
        {
            return new ScaledObjectTriggerV1Alpha1
            {
                Type = GetKedaTriggerType(binding["type"]?.ToString()),
                Metadata = PopulateMetadataDictionary(binding)
            };
        }

        public override IDictionary<string, string> PopulateMetadataDictionary(JToken t)
        {
            const string ConnectionField = "connection";
            const string ConnectionFromEnvField = "connectionFromEnv";

            IDictionary<string, string> metadata = t.ToObject<Dictionary<string, JToken>>()
                .Where(i => i.Value.Type == JTokenType.String)
                .ToDictionary(k => k.Key, v => v.Value.ToString());

            var triggerType = t["type"].ToString().ToLower();

            switch (triggerType)
            {
                case TriggerTypes.AzureBlobStorage:
                case TriggerTypes.AzureStorageQueue:
                    metadata[ConnectionFromEnvField] = metadata[ConnectionField] ?? "AzureWebJobsStorage";
                    metadata.Remove(ConnectionField);
                    break;
                case TriggerTypes.AzureServiceBus:
                    metadata[ConnectionFromEnvField] = metadata[ConnectionField] ?? "AzureWebJobsServiceBus";
                    metadata.Remove(ConnectionField);
                    break;
                case TriggerTypes.AzureEventHubs:
                    metadata[ConnectionFromEnvField] = metadata[ConnectionField];
                    metadata.Remove(ConnectionField);
                    break;

                case TriggerTypes.Kafka:
                    metadata["bootstrapServers"] = metadata["brokerList"];
                    metadata.Remove("brokerList");
                    metadata.Remove("protocol");
                    metadata.Remove("authenticationMode");
                    break;

                case TriggerTypes.RabbitMq:
                    metadata["hostFromEnv"] = metadata["connectionStringSetting"];
                    metadata.Remove("connectionStringSetting");
                    break;
            }

            // Clean-up for all triggers

            metadata.Remove("type");
            metadata.Remove("name");

            return metadata;
        }
    }
}