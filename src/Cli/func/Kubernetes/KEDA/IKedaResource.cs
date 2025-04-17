// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public interface IKedaResource
    {
        internal IKubernetesResource GetKubernetesResource(string name, string @namespace, TriggersPayload triggers, DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas);

        internal IDictionary<string, string> PopulateMetadataDictionary(JToken t);

        internal string GetKedaTriggerType(string triggerType);
    }
}
