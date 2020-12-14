using Azure.Functions.Cli.Kubernetes.KEDA.V1;
using Azure.Functions.Cli.Kubernetes.KEDA.V2;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public class KedaResourceFactory
    {
        private readonly KedaVersion? _kedaVersion;

        public KedaResourceFactory(KedaVersion? kedaVersion)
        {
            _kedaVersion = kedaVersion;
        }

        public IKubernetesResource Create(string name, string @namespace, TriggersPayload triggers,
               DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas)
        {
            switch (_kedaVersion)
            {
                case KedaVersion.v1:
                    return new KedaV1Resource().GetKubernetesResource(name, @namespace, triggers, deployment, pollingInterval, cooldownPeriod, minReplicas, maxReplicas);
                case KedaVersion.v2:
                    return new KedaV2Resource().GetKubernetesResource(name, @namespace, triggers, deployment, pollingInterval, cooldownPeriod, minReplicas, maxReplicas);
                default:
                    throw new ArgumentOutOfRangeException(nameof(_kedaVersion), _kedaVersion, "Specified KEDA version is not supported");
            }
        }

        public static IDictionary<string, string> PopulateMetadataDictionary(JToken t)
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

        public static string GetKedaTrigger(string triggerType)
        {
            if (string.IsNullOrEmpty(triggerType))
            {
                throw new ArgumentNullException(nameof(triggerType));
            }

            triggerType = triggerType.ToLower();

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
