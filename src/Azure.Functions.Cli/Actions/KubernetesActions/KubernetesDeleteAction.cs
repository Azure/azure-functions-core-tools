using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.KEDA;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Colors.Net;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Actions.KubernetesActions
{
    [Action(Name = "delete", Context = Context.Kubernetes, HelpText = "")]
    class KubernetesDeleteAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string Registry { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = "default";
        public string PullSecret { get; set; } = string.Empty;
        public bool UseConfigMap { get; set; }
        public bool NoDocker { get; set; }
        public bool DryRun { get; private set; }
        public string ImageName { get; private set; }
        public string ConfigMapName { get; private set; }
        public string SecretsCollectionName { get; private set; }
        public string KeysSecretCollectionName { get; private set; }
        public bool MountFuncKeysAsContainerVolume { get; private set; }
        public int? PollingInterval { get; private set; }
        public int? CooldownPeriod { get; private set; }
        public string ServiceType { get; set; } = "LoadBalancer";
        public IEnumerable<string> ServiceTypes { get; set; } = new string[] { "ClusterIP", "NodePort", "LoadBalancer" };
        public bool IgnoreErrors { get; private set; } = false;
        public int? MaxReplicaCount { get; private set; }
        public int? MinReplicaCount { get; private set; }
        public KedaVersion? KedaVersion { get; private set; } = Kubernetes.KEDA.KedaVersion.v2;
        public bool ShowServiceFqdn { get; set; } = false;

        public KubernetesDeleteAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("name", "The name used for the deployment and other artifacts in kubernetes", n =>
            {
                KubernetesHelper.ValidateKubernetesName(n);
                Name = n;
            }, isRequired: true);
            SetFlag<string>("image-name", "Image to use for the pod deployment and to read functions from", n => ImageName = n);
            SetFlag<KedaVersion>("keda-version", $"Defines the version of KEDA to use. Default: {Kubernetes.KEDA.KedaVersion.v2}. Options are: {Kubernetes.KEDA.KedaVersion.v1} or {Kubernetes.KEDA.KedaVersion.v2}", n => KedaVersion = n);
            SetFlag<string>("registry", "When set, a docker build is run and the image is pushed to that registry/name. This is mutually exclusive with --image-name. For docker hub, use username.", r => Registry = r);
            SetFlag<string>("namespace", "Kubernetes namespace to deploy to. Default: default", ns => Namespace = ns);
            SetFlag<int>("polling-interval", "The polling interval for checking non-http triggers. Default: 30 (seconds)", p => PollingInterval = p);
            SetFlag<int>("cooldown-period", "The cooldown period for the deployment before scaling back to 0 after all triggers are no longer active. Default: 300 (seconds)", p => CooldownPeriod = p);
            SetFlag<int>("min-replicas", "Minimum replica count", m => MinReplicaCount = m);
            SetFlag<int>("max-replicas", "Maximum replica count to scale to by HPA", m => MaxReplicaCount = m);
            SetFlag<string>("keys-secret-name", "The name of a kubernetes secret collection to use for the function app keys (host keys, function keys etc.)", ksn => KeysSecretCollectionName = ksn);
            SetFlag<bool>("mount-funckeys-as-containervolume", "The flag indicating to mount the func app keys as container volume", kmv => MountFuncKeysAsContainerVolume = kmv);
            SetFlag<string>("secret-name", "The name of an existing kubernetes secret collection, containing func app settings, to use in the deployment instead of creating new a new one based upon local.settings.json", sn => SecretsCollectionName = sn);
            SetFlag<string>("config-map-name", "The name of an existing config map with func app settings to use in the deployment", cm => ConfigMapName = cm);
            SetFlag<string>("service-type", "Kubernetes Service Type. Default LoadBalancer  Valid options: " + string.Join(",", ServiceTypes), s =>
            {
                if (!string.IsNullOrEmpty(s) && !ServiceTypes.Contains(s))
                {
                    throw new CliArgumentsException($"serviceType {ServiceType} is not supported. Valid options are: {string.Join(",", ServiceTypes)}");
                }
                ServiceType = s;
            });
            SetFlag<bool>("use-config-map", "Use a ConfigMap/V1 instead of a Secret/V1 object for function app settings configurations", c => UseConfigMap = c);
            SetFlag<bool>("dry-run", "Show the deployment template", f => DryRun = f);
            SetFlag<bool>("ignore-errors", "Proceed with the deployment if a resource returns an error. Default: false", f => IgnoreErrors = f);
            SetFlag<bool>("show-service-fqdn", "display Http Trigger URL with kubernetes FQDN rather than IP. Default: false", f => ShowServiceFqdn = f);
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            (var resolvedImageName, var shouldBuild) = ResolveImageName();
            var triggers = await DockerHelpers.GetTriggersFromDockerImage(resolvedImageName);
            (var resources, var funcKeys) = await KubernetesHelper.GetFunctionsDeploymentResources(
                Name,
                resolvedImageName,
                Namespace,
                triggers,
                _secretsManager.GetSecrets(),
                PullSecret,
                SecretsCollectionName,
                ConfigMapName,
                UseConfigMap,
                PollingInterval,
                CooldownPeriod,
                ServiceType,
                MinReplicaCount,
                MaxReplicaCount,
                KeysSecretCollectionName,
                MountFuncKeysAsContainerVolume,
                KedaVersion);

            if (DryRun)
            {
                ColoredConsole.WriteLine(KubernetesHelper.SerializeResources(resources, OutputSerializationOptions.Yaml));
            }
            else
            {
                var tasks = new List<Task>();
                foreach (var resource in resources)
                {
                    tasks.Add(KubectlHelper.KubectlDelete(resource, showOutput: true, ignoreError: IgnoreErrors, @namespace: Namespace));
                }
                Task.WaitAll(tasks.ToArray());
            }
        }

        private (string, bool) ResolveImageName()
        {
            if (!string.IsNullOrEmpty(Registry))
            {
                return ($"{Registry}/{Name}", true && !NoDocker);
            }
            else if (!string.IsNullOrEmpty(ImageName))
            {
                return (ImageName, false);
            }
            throw new CliArgumentsException("either --image-name or --registry is required.");
        }
    }
}