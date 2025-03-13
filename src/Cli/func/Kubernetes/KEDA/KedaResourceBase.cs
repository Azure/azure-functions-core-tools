using System;
using System.Collections.Generic;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public abstract class KedaResourceBase : IKedaResource
    {
        public abstract IDictionary<string, string> PopulateMetadataDictionary(JToken t);

        public virtual string GetKedaTriggerType(string triggerType)
        {
            if (string.IsNullOrEmpty(triggerType))
            {
                throw new ArgumentNullException(nameof(triggerType));
            }

            triggerType = triggerType.ToLower();

            switch (triggerType)
            {
                case TriggerTypes.AzureStorageQueue:
                    return "azure-queue";

                case TriggerTypes.Kafka:
                    return "kafka";

                case TriggerTypes.AzureBlobStorage:
                    return "azure-blob";

                case TriggerTypes.AzureServiceBus:
                    return "azure-servicebus";

                case TriggerTypes.AzureEventHubs:
                    return "azure-eventhub";

                case TriggerTypes.RabbitMq:
                    return "rabbitmq";

                default:
                    return triggerType;
            }
        }

        public abstract IKubernetesResource GetKubernetesResource(string name, string @namespace, TriggersPayload triggers, DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas);
    }
}
