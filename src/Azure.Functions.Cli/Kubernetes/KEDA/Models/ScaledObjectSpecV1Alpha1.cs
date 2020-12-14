﻿using Azure.Functions.Cli.Kubernetes.KEDA;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public class ScaledObjectSpecV1Alpha1 : IKubernetesSpec
    {
        [JsonProperty("scaleTargetRef")]
        public ScaledObjectScaleTargetRefV1Alpha1 ScaleTargetRef { get; set; }

        [JsonProperty("pollingInterval")]
        public int? PollingInterval { get; set; }

        [JsonProperty("cooldownPeriod")]
        public int? CooldownPeriod { get; set; }

        [JsonProperty("minReplicaCount")]
        public int? MinReplicaCount { get; set; }

        [JsonProperty("maxReplicaCount")]
        public int? MaxReplicaCount { get; set; }

        [JsonProperty("triggers")]
        public IEnumerable<ScaledObjectTriggerV1Alpha1> Triggers { get; internal set; }
    }
}
