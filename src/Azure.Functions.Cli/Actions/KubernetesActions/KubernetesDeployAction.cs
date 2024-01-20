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
using Microsoft.Extensions.FileSystemGlobbing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Actions.KubernetesActions
{
    [Action(Name = "deploy", Context = Context.Kubernetes, HelpText = "")]
    class KubernetesDeployAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string Registry { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; }
        public string PullSecret { get; set; } = string.Empty;
        public bool NoDocker { get; set; }
        public bool UseConfigMap { get; set; }
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
        public bool WriteConfigs { get; set; } = false;
        public string ConfigFile { get; set; } = "functions.yaml";
        public bool UseGitHashAsImageVersion { get; set; } = false;
        public string HashFilesPattern { get; set; } = "";
        public bool BuildImage { get; set; } = true;

        public IDictionary<string, string> KeySecretsAnnotations { get; private set; }

        public KubernetesDeployAction(ISecretsManager secretsManager)
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
            SetFlag<string>("namespace", "Kubernetes namespace to deploy to. Default: Current namespace in Kubernetes config.", ns => Namespace = ns);
            SetFlag<string>("pull-secret", "The secret holding a private registry credentials", s => PullSecret = s);
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
            SetFlag<bool>("no-docker", "With --image-name, the core-tools will inspect the functions inside the image. This will require mounting the image filesystem. Passing --no-docker uses current directory for functions.", nd => NoDocker = nd);
            SetFlag<bool>("use-config-map", "Use a ConfigMap/V1 instead of a Secret/V1 object for function app settings configurations", c => UseConfigMap = c);
            SetFlag<bool>("dry-run", "Show the deployment template", f => DryRun = f);
            SetFlag<bool>("ignore-errors", "Proceed with the deployment if a resource returns an error. Default: false", f => IgnoreErrors = f);
            SetFlag<bool>("show-service-fqdn", "display Http Trigger URL with kubernetes FQDN rather than IP. Default: false", f => ShowServiceFqdn = f);
            SetFlag<bool>("use-git-hash-version", "Use the githash as the version for the image", f => UseGitHashAsImageVersion = f);
            SetFlag<bool>("write-configs", "Output the kubernetes configurations as YAML files instead of deploying", f => WriteConfigs = f);
            SetFlag<string>("config-file", "if --write-configs is true, write configs to this file (default: 'functions.yaml')", f => ConfigFile = f);
            SetFlag<string>("hash-files", "Files to hash to determine the image version", f => HashFilesPattern = f);
            SetFlag<bool>("image-build", "If true, skip the docker build", f => BuildImage = f);
            SetFlag<string>("keys-secret-annotations", "The annotations to add to the keys secret e.g. key1=val1,key2=val2", a => KeySecretsAnnotations = a.Split(',').Select(s => s.Split('=')).ToDictionary(k => k[0], v => v[1]));

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            (var resolvedImageName, var shouldBuild) = await ResolveImageName();
            TriggersPayload triggers = null;
            if (DryRun)
            {
                if (shouldBuild)
                {
                    // don't build on a --dry-run.
                    // read files from the local dir
                    triggers = await GetTriggersLocalFiles();
                }
                else
                {
                    triggers = await DockerHelpers.GetTriggersFromDockerImage(resolvedImageName);
                }
            }
            else if (BuildImage)
            {
                if (shouldBuild)
                {
                    await DockerHelpers.DockerBuild(resolvedImageName, Environment.CurrentDirectory);
                }
                // This needs to be fixed to run after the build.
                triggers = await DockerHelpers.GetTriggersFromDockerImage(resolvedImageName);
            }
            else
            {
                triggers = await GetTriggersLocalFiles();
            }

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
                KedaVersion,
                KeySecretsAnnotations
                );

            if (DryRun)
            {
                ColoredConsole.WriteLine(KubernetesHelper.SerializeResources(resources, OutputSerializationOptions.Yaml));
            }
            else
            {
                Task kubernetesTask = null;
                Task imageTask = ((BuildImage && shouldBuild) ? DockerHelpers.DockerPush(resolvedImageName, false) : null);

                if (WriteConfigs)
                {
                    var yaml = KubernetesHelper.SerializeResources(resources, OutputSerializationOptions.Yaml);
                    kubernetesTask = File.WriteAllTextAsync(ConfigFile, yaml);
                    Console.Write($"Configuration written to {ConfigFile}");
                    return;
                }
                else
                {
                    Func<Task> resourceTaskFn = () => {
                        var serialized = KubernetesHelper.SerializeResources(resources, OutputSerializationOptions.Yaml);
                        return KubectlHelper.KubectlApply(serialized, showOutput: true, ignoreError: IgnoreErrors, @namespace: Namespace);
                    };

                    if (!await KubernetesHelper.NamespaceExists(Namespace))
                    {
                        kubernetesTask = KubernetesHelper.CreateNamespace(Namespace).ContinueWith((result) => {
                            return resourceTaskFn();
                        });
                    }
                    else
                    {
                        kubernetesTask = resourceTaskFn();
                    }
                }

                if (imageTask != null)
                {
                    await imageTask;
                }
                await kubernetesTask;

                var httpService = resources
                    .Where(i => i is ServiceV1)
                    .Cast<ServiceV1>()
                    .FirstOrDefault(s => s.Metadata.Name.Contains("http"));
                var httpDeployment = resources
                    .Where(i => i is DeploymentV1Apps)
                    .Cast<DeploymentV1Apps>()
                    .FirstOrDefault(d => d.Metadata.Name.Contains("http"));

                if (httpDeployment != null)
                {
                    await KubernetesHelper.WaitForDeploymentRollout(httpDeployment);
                    //Print the function keys message to the console
                    await KubernetesHelper.PrintFunctionsInfo(httpDeployment, httpService, funcKeys, triggers, ShowServiceFqdn);
                }
            }
        }

        private async Task<TriggersPayload> GetTriggersLocalFiles()
        {
            var functionsPath = Environment.CurrentDirectory;
            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet ||
                GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnetIsolated)
            {
                if (DotnetHelpers.CanDotnetBuild())
                {
                    var outputPath = Path.Combine("bin", "output");
                    await DotnetHelpers.BuildDotnetProject(outputPath, string.Empty, showOutput: false);
                    functionsPath = Path.Combine(Environment.CurrentDirectory, outputPath);
                }
            }

            var functionsJsons = GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnetIsolated
                ? ReadFunctionsMetadata(functionsPath)
                : ReadFunctionJsons(functionsPath);

            var hostJson = JsonConvert.DeserializeObject<JObject>(FileSystemHelpers.ReadAllTextFromFile("host.json"));

            return new TriggersPayload
            {
                HostJson = hostJson,
                FunctionsJson = functionsJsons
            };
        }

        private static Dictionary<string, JObject> ReadFunctionsMetadata(string functionsPath)
        {
            var functionsMetadataPath = Path.Combine(functionsPath, "functions.metadata");

            if (!FileSystemHelpers.FileExists(functionsMetadataPath))
            {
                return new();
            }

            var functionsMetadataContents = FileSystemHelpers.ReadAllTextFromFile(functionsMetadataPath);
            var functionsMetadata = JsonConvert.DeserializeObject<JArray>(functionsMetadataContents);

            return functionsMetadata
                .Where(x => x["bindings"] != null)
                .ToDictionary(k => k["name"].ToString(), v => v.ToObject<JObject>());
        }

        private static Dictionary<string, JObject> ReadFunctionJsons(string functionsPath)
        {
            var functionJsonFiles = FileSystemHelpers
                .GetDirectories(functionsPath)
                .Select(d => Path.Combine(d, "function.json"))
                .Where(FileSystemHelpers.FileExists)
                .Select(f => (filePath: f, content: FileSystemHelpers.ReadAllTextFromFile(f)));

            return functionJsonFiles
                .Select(t => (t.filePath, jObject: JsonConvert.DeserializeObject<JObject>(t.content)))
                .Where(b => b.jObject["bindings"] != null)
                .ToDictionary(k => Path.GetFileName(Path.GetDirectoryName(k.filePath)), v => v.jObject);
        }

        private async Task<(string, bool)> ResolveImageName()
        {
            var version = "latest";
            if (UseGitHashAsImageVersion) {
                if (HashFilesPattern.Length > 0)
                {
                    var matcher = new Matcher();
                    matcher.AddInclude(HashFilesPattern);
                    var matches = MatcherExtensions.GetResultsInFullPath(matcher, "./");
                    version = GitHelpers.ActionsHashFiles(matches);
                }
                else
                {
                    (var stdout, var err, var exit) = await GitHelpers.GitHash();
                    if (exit != 0) {
                        throw new CliException("Git describe failed: " + err);
                    }
                    version = stdout;
                }
            }
            if (!string.IsNullOrEmpty(Registry))
            {
                return ($"{Registry}/{Name}:{version}", true && !NoDocker);
            }
            else if (!string.IsNullOrEmpty(ImageName))
            {
                return ($"{ImageName}", false);
            }
            throw new CliArgumentsException("either --image-name or --registry is required.");
        }
    }
}
