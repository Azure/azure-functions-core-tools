using System.Collections.Generic;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public interface IKedaResource
    {
        IKubernetesResource GetKubernetesResource(string name, string @namespace, TriggersPayload triggers, DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas);

        IDictionary<string, string> PopulateMetadataDictionary(JToken t);
        string GetKedaTriggerType(string triggerType);
    }
}
