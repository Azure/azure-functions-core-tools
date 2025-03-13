
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class ContainerV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("image")]
        public string Image { get; set; }

        [JsonProperty("resources")]
        public ContainerResourcesV1 Resources { get; set; }

        [JsonProperty("ports")]
        public IEnumerable<ContainerPortV1> Ports { get; set; }

        [JsonProperty("env")]
        public IEnumerable<ContainerEnvironmentV1> Env { get; set; }

        [JsonProperty("envFrom")]
        public IEnumerable<ContainerEnvironmentFromV1> EnvFrom { get; set; }

        [JsonProperty("imagePullPolicy")]
        public string ImagePullPolicy { get; internal set; }

        [JsonProperty("volumeMounts")]
        public IEnumerable<ContainerVolumeMountV1> VolumeMounts { get; internal set; }

        [JsonProperty("readinessProbe")]
        public Probe ReadinessProbe { get; set;}

        [JsonProperty("startupProbe")]
        public Probe StartupProbe { get; set;}
    }

    public class ContainerVolumeMountV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("mountPath")]
        public string MountPath { get; set; }
    }

    public class ContainerResourceRequestsV1
    {
        [JsonProperty("memory")]
        public string Memory { get; set; }

        [JsonProperty("cpu")]
        public string Cpu { get; set; }
    }

    public class ContainerResourcesV1
    {
        [JsonProperty("requests")]
        public ContainerResourceRequestsV1 Requests { get; set; }
    }


    public class ContainerPortV1
    {
        [JsonProperty("containerPort")]
        public int ContainerPort { get; set; }
    }

    public class ContainerEnvironmentV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("valueFrom")]
        public EnvironmentValueFromV1 ValueFrom { get; set; }
    }

    public class EnvironmentValueFromV1
    {
        [JsonProperty("secretKeyRef")]
        public EnvironmentNameKeyPairV1 SecretKeyRef { get; set; }

        [JsonProperty("configMapKeyRef")]
        public EnvironmentNameKeyPairV1 ConfigMapKeyRef { get; set; }
    }

    public class EnvironmentNameKeyPairV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }
    }

    public class ContainerEnvironmentFromV1
    {
        [JsonProperty("configMapRef")]
        public NamedObjectV1 ConfigMapRef { get; set; }

        [JsonProperty("secretRef")]
        public NamedObjectV1 SecretRef { get; set; }
    }

    public class NamedObjectV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class VolumeMountV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("mountPath")]
        public string MountPath { get; set; }

        [JsonProperty("readOnly")]
        public bool ReadOnly { get; set; }
    }

    public class Probe
    {
        [JsonProperty("failureThreshold")]
        public int? FailureThreshold { get; set; }

        [JsonProperty("periodSeconds")]
        public int? PeriodSeconds { get; set; }

        [JsonProperty("successThreshold")]
        public int? SuccessThreshold { get; set; }

        [JsonProperty("timeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        [JsonProperty("httpGet")]
        public HttpAction HttpGet { get; set; }
    }

    public class HttpAction
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("port")]
        public int? port { get; set; }

        [JsonProperty("scheme")]
        public string Scheme { get; set; }
    }
}