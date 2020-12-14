using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public class KedaResource
    {
        public virtual IDictionary<string, string> PopulateMetadataDictionary(JToken t)
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

        public virtual string GetKedaTriggerType(string triggerType)
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
