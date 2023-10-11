using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Kubernetes.FuncKeys;
using Azure.Functions.Cli.Kubernetes.KEDA;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Colors.Net;
using Newtonsoft.Json;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;
using static Azure.Functions.Cli.Common.OutputTheme;
using Constants = Azure.Functions.Cli.Common.Constants;

namespace Azure.Functions.Cli.Kubernetes
{
    public static class KubernetesHelper
    {
        public static void ValidateKubernetesName(string name)
        {
            var regExValue = "^[a-z0-9\\-\\.]*$";
            var regEx = new Regex(regExValue);
            const string kNameDoc = "See: https://kubernetes.io/docs/concepts/overview/working-with-objects/names";
            if (name.Length > 253)
            {
                throw new CliException("Kubernetes name must be < 253 characters.\n" + kNameDoc);
            }
            else if (!regEx.IsMatch(name))
            {
                throw new CliException($"Kubernetes name must match {regExValue}.\n" + kNameDoc);
            }
        }

        internal static async Task<bool> NamespaceExists(string @namespace)
        {
            if (string.IsNullOrWhiteSpace(@namespace))
            {
                // No namespace was specified so we rely on the default namespace in .kube/config
                // Because of that, we assume that it exists because we don't know its name

                return true;
            }

            (_, _, var exitCode) = await KubectlHelper.RunKubectl($"get namespace {@namespace}", ignoreError: true, showOutput: false);
            return exitCode == 0;
        }

        internal static async Task<(string Output, bool ResourceExists)> ResourceExists(string resourceTypeName, string resourceName, string @namespace, bool returnJsonOutput = false)
        {
            var cmd = $"get {resourceTypeName} {resourceName}";

            // If a namespace is specified, then we need to filter
            if (string.IsNullOrWhiteSpace(@namespace) == false)
            {
                cmd += $" --namespace {@namespace}";
            }

            if (returnJsonOutput)
            {
                cmd = string.Concat(cmd, " -o json");
            }

            (string output, _, var exitCode) = await KubectlHelper.RunKubectl(cmd, ignoreError: true, showOutput: false);
            return (output, exitCode == 0);
        }

        internal static async Task CreateNamespace(string @namespace)
        {
            if (string.IsNullOrWhiteSpace(@namespace))
            {
                // No namespace was specified so we rely on the default namespace in .kube/config
                // Because of that, we assume that already exists since we don't know its name

                return;
            }

            await KubectlHelper.RunKubectl($"create namespace {@namespace}", ignoreError: false, showOutput: true);
        }

        internal static async Task<(IEnumerable<IKubernetesResource>, IDictionary<string, string>)> GetFunctionsDeploymentResources(
            string name,
            string imageName,
            string @namespace,
            TriggersPayload triggers,
            IDictionary<string, string> secrets,
            string pullSecret = null,
            string secretsCollectionName = null,
            string configMapName = null,
            bool useConfigMap = false,
            int? pollingInterval = null,
            int? cooldownPeriod = null,
            string serviceType = "LoadBalancer",
            int? minReplicas = null,
            int? maxReplicas = null,
            string keysSecretCollectionName = null,
            bool mountKeysAsContainerVolume = false,
            KedaVersion? kedaVersion = null,
            IDictionary<string, string> keySecretsAnnotations = null)
        {
            IKubernetesResource scaledObject = null;
            var result = new List<IKubernetesResource>();
            var deployments = new List<DeploymentV1Apps>();
            var httpFunctions = triggers.FunctionsJson
                .Where(b => b.Value["bindings"]?.Any(e => e?["type"].ToString().IndexOf("httpTrigger", StringComparison.OrdinalIgnoreCase) != -1) == true);
            var nonHttpFunctions = triggers.FunctionsJson.Where(f => httpFunctions.All(h => h.Key != f.Key));
            keysSecretCollectionName = string.IsNullOrEmpty(keysSecretCollectionName)
                ? $"func-keys-kube-secret-{name}"
                : keysSecretCollectionName;
            if (httpFunctions.Any())
            {
                int position = 0;
                var enabledFunctions = httpFunctions.ToDictionary(k => $"AzureFunctionsJobHost__functions__{position++}", v => v.Key);
                //Environment variables for the func app keys kubernetes secret
                var kubernetesSecretEnvironmentVariable = FuncAppKeysHelper.FuncKeysKubernetesEnvironVariables(keysSecretCollectionName, mountKeysAsContainerVolume);
                var additionalEnvVars = enabledFunctions.Concat(kubernetesSecretEnvironmentVariable).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var deployment = GetDeployment(name + "-http", @namespace, imageName, pullSecret, 1, additionalEnvVars, port: 80);
                deployments.Add(deployment);
                var service = GetService(name + "-http", @namespace, deployment, serviceType);
                result.Add(service);
            }

            if (nonHttpFunctions.Any())
            {
                int position = 0;
                var enabledFunctions = nonHttpFunctions.ToDictionary(k => $"AzureFunctionsJobHost__functions__{position++}", v => v.Key);
                var deployment = GetDeployment(name, @namespace, imageName, pullSecret, minReplicas ?? 0, enabledFunctions);
                deployments.Add(deployment);
                scaledObject = await KedaHelper.GetScaledObject(name, @namespace, triggers, deployment, pollingInterval, cooldownPeriod, minReplicas, maxReplicas, kedaVersion);
            }

            // Set worker runtime if needed.
            if (!secrets.ContainsKey(Constants.FunctionsWorkerRuntime))
            {
                secrets[Constants.FunctionsWorkerRuntime] = GlobalCoreToolsSettings.CurrentWorkerRuntime.ToString();
            }

            int resourceIndex = 0;
            if (useConfigMap)
            {
                var configMap = GetConfigMap(name, @namespace, secrets);
                result.Insert(resourceIndex, configMap);
                resourceIndex++;
                foreach (var deployment in deployments)
                {
                    deployment.Spec.Template.Spec.Containers.First().EnvFrom = new ContainerEnvironmentFromV1[]
                    {
                        new ContainerEnvironmentFromV1
                        {
                            ConfigMapRef = new NamedObjectV1
                            {
                                Name = configMap.Metadata.Name
                            }
                        }
                    };
                }
            }
            else if (!string.IsNullOrEmpty(secretsCollectionName))
            {
                foreach (var deployment in deployments)
                {
                    deployment.Spec.Template.Spec.Containers.First().EnvFrom = new ContainerEnvironmentFromV1[]
                    {
                        new ContainerEnvironmentFromV1
                        {
                            SecretRef = new NamedObjectV1
                            {
                                Name = secretsCollectionName
                            }
                        }
                    };
                }
            }
            else if (!string.IsNullOrEmpty(configMapName))
            {
                foreach (var deployment in deployments)
                {
                    deployment.Spec.Template.Spec.Containers.First().EnvFrom = new ContainerEnvironmentFromV1[]
                    {
                        new ContainerEnvironmentFromV1
                        {
                            ConfigMapRef = new NamedObjectV1
                            {
                                Name = configMapName
                            }
                        }
                    };
                }
            }
            else
            {
                var secret = GetSecret(name, @namespace, secrets);
                result.Insert(resourceIndex, secret);
                resourceIndex++;
                foreach (var deployment in deployments)
                {
                    deployment.Spec.Template.Spec.Containers.First().EnvFrom = new ContainerEnvironmentFromV1[]
                    {
                        new ContainerEnvironmentFromV1
                        {
                            SecretRef = new NamedObjectV1
                            {
                                Name = secret.Metadata.Name
                            }
                        }
                    };
                }
            }

            IDictionary<string, string> resultantFunctionKeys = new Dictionary<string, string>();
            if (httpFunctions.Any())
            {
                var currentImageFuncKeys = FuncAppKeysHelper.CreateKeys(httpFunctions.Select(f => f.Key));
                resultantFunctionKeys = GetFunctionKeys(currentImageFuncKeys, await GetExistingFunctionKeys(keysSecretCollectionName, @namespace));
                if (resultantFunctionKeys.Any())
                {
                    result.Insert(resourceIndex, GetSecret(keysSecretCollectionName, @namespace, resultantFunctionKeys, annotations: keySecretsAnnotations));
                    resourceIndex++;
                }

                //if function keys Secrets needs to be mounted as volume in the function runtime container
                if (mountKeysAsContainerVolume)
                {
                    FuncAppKeysHelper.CreateFuncAppKeysVolumeMountDeploymentResource(deployments, keysSecretCollectionName);
                }
                //Create the Pod identity with the role to modify the function kubernetes secret
                else
                {
                    var svcActName = $"{name}-function-keys-identity-svc-act";
                    var svcActDeploymentResource = GetServiceAccount(svcActName, @namespace);
                    result.Insert(resourceIndex, svcActDeploymentResource);
                    resourceIndex++;

                    var funcKeysManagerRoleName = "functions-keys-manager-role";
                    var secretManagerRole = GetSecretManagerRole(funcKeysManagerRoleName, @namespace);
                    result.Insert(resourceIndex, secretManagerRole);
                    resourceIndex++;
                    var roleBindingName = $"{svcActName}-functions-keys-manager-rolebinding";
                    var funcKeysRoleBindingDeploymentResource = GetRoleBinding(roleBindingName, @namespace, funcKeysManagerRoleName, svcActName);
                    result.Insert(resourceIndex, funcKeysRoleBindingDeploymentResource);
                    resourceIndex++;

                    //add service account identity to the pod
                    foreach (var deployment in deployments)
                    {
                        deployment.Spec.Template.Spec.ServiceAccountName = svcActName;
                    }
                }
            }

            result = result.Concat(deployments).ToList();
            return (scaledObject != null ? result.Append(scaledObject) : result, resultantFunctionKeys);
        }

        internal static async Task WaitForDeploymentRollout(DeploymentV1Apps deployment)
        {
            var statement = $"rollout status deployment {deployment.Metadata.Name}";

            // If a namespace is specified, we filter on it
            if (string.IsNullOrWhiteSpace(deployment.Metadata.Namespace) == false)
            {
                statement += $" --namespace {deployment.Metadata.Namespace}";
            }

            await KubectlHelper.RunKubectl(statement, showOutput: true, timeout: TimeSpan.FromMinutes(4));
        }

        private static async Task<IDictionary<string, string>> GetExistingFunctionKeys(string keysSecretCollectionName, string @namespace)
        {
            if (string.IsNullOrWhiteSpace(keysSecretCollectionName)
                || string.IsNullOrWhiteSpace(@namespace))
            {
                return new Dictionary<string, string>();
            }

            (string output, bool keysSecretExist) = await ResourceExists("secret", keysSecretCollectionName, @namespace, true);
            if (keysSecretExist)
            {
                var allExistingFuncKeys = TryParse<SecretsV1>(output);
                if (allExistingFuncKeys?.Data?.Any() == true)
                {
                    return allExistingFuncKeys.Data.ToDictionary(k => k.Key, v => Encoding.UTF8.GetString(Convert.FromBase64String(v.Value)));
                }
            }

            return new Dictionary<string, string>();
        }

        internal static async Task PrintFunctionsInfo(DeploymentV1Apps deployment, ServiceV1 service, IDictionary<string, string> funcKeys, TriggersPayload triggers, bool showServiceFqdn)
        {
            if (funcKeys?.Any() == false || triggers == null)
            {
                return;
            }

            var httpFunctions = triggers.FunctionsJson
                .Where(b => b.Value["bindings"]?.Any(e => e?["type"].ToString().IndexOf("httpTrigger", StringComparison.OrdinalIgnoreCase) != -1) == true)
                .Select(item => item.Key);

            var localPort = NetworkHelpers.GetAvailablePort();
            Process proxy = null;
            try
            {
                proxy = await KubectlHelper.RunKubectlProxy(
                    deployment,
                    service.Spec.Ports.FirstOrDefault()?.Port ?? 80,
                    localPort
                );
                string baseAddress;
                if (showServiceFqdn)
                {
                    baseAddress = $"{service.Metadata.Name}.{service.Metadata.Namespace}.svc.cluster.local";
                }
                else
                {
                    baseAddress = await GetServiceIp(service);
                }

                var masterKey = funcKeys["host.master"];
                if (httpFunctions?.Any() == true)
                {
                    foreach (var functionName in httpFunctions)
                    {
                        var getFunctionAdminUri = $"http://127.0.0.1:{localPort}/admin/functions/{functionName}?code={masterKey}";
                        var httpResponseMessage = await GetHttpResponse(new HttpRequestMessage(HttpMethod.Get, getFunctionAdminUri), 20);
                        httpResponseMessage.EnsureSuccessStatusCode();

                        var responseContent = await httpResponseMessage.Content.ReadAsStringAsync();
                        var functionsInfo = JsonConvert.DeserializeObject<FunctionInfo>(responseContent);

                        var trigger = functionsInfo
                            .Config?["bindings"]
                            ?.FirstOrDefault(o => o["type"]?.ToString().IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) != -1)
                            ?["type"];

                        trigger = trigger ?? "No Trigger Found";
                        var showFunctionKey = true;

                        var authLevel = functionsInfo
                            .Config?["bindings"]
                            ?.FirstOrDefault(o => !string.IsNullOrEmpty(o["authLevel"]?.ToString()))
                            ?["authLevel"];

                        if (authLevel != null && authLevel.ToString().Equals("anonymous", StringComparison.OrdinalIgnoreCase))
                        {
                            showFunctionKey = false;
                        }

                        ColoredConsole.WriteLine($"\t{functionName} - [{VerboseColor(trigger.ToString())}]");
                        if (!string.IsNullOrEmpty(functionsInfo.InvokeUrlTemplate))
                        {
                            var url = new Uri(new Uri($"http://{baseAddress}"), new Uri(functionsInfo.InvokeUrlTemplate).PathAndQuery).ToString();
                            if (showFunctionKey)
                            {
                                ColoredConsole.WriteLine($"\tInvoke url: {VerboseColor($"{url}?code={funcKeys[$"functions.{functionName.ToLower()}.default"]}")}");
                            }
                            else
                            {
                                ColoredConsole.WriteLine($"\tInvoke url: {VerboseColor(url)}");
                            }
                        }
                        ColoredConsole.WriteLine();

                    }
                }
            }
            finally
            {
                if (proxy != null && !proxy.HasExited)
                {
                    proxy.Kill();
                }
            }

            //Print the master key as well for the user
            ColoredConsole.WriteLine($"\tMaster key: {VerboseColor($"{funcKeys["host.master"]}")}");
        }

        private static async Task<HttpResponseMessage> GetHttpResponse(HttpRequestMessage httpRequestMessage, int retryCount = 5)
        {
            HttpResponseMessage httpResponseMsg = new HttpResponseMessage();
            if (httpRequestMessage == null)
            {
                return httpResponseMsg;
            }

            int currentRetry = 0;
            using (var httpClient = new HttpClient(new HttpClientHandler()))
            {
                httpClient.Timeout = TimeSpan.FromSeconds(2);
                while (currentRetry++ < retryCount)
                {
                    try
                    {
                        httpResponseMsg = await httpClient.SendAsync(httpRequestMessage.Clone());
                        if (httpResponseMsg.IsSuccessStatusCode ||
                            (httpResponseMsg.StatusCode != System.Net.HttpStatusCode.BadGateway
                            && httpResponseMsg.StatusCode != System.Net.HttpStatusCode.RequestTimeout
                            && httpResponseMsg.StatusCode != System.Net.HttpStatusCode.GatewayTimeout
                            && httpResponseMsg.StatusCode != System.Net.HttpStatusCode.NotFound))
                        {
                            return httpResponseMsg;
                        }
                    }
                    catch (Exception e)
                    {
                        if (StaticSettings.IsDebug)
                        {
                            ColoredConsole.Error.WriteLine(e);
                        }
                    }
                    await Task.Delay(new Random().Next(500, 2000));
                }
            }

            return httpResponseMsg;
        }

        private static async Task<string> GetServiceIp(ServiceV1 service, int retryCount = 12)
        {
            int currentRetry = 0;
            while (currentRetry++ < retryCount)
            {
                (string output, bool serviceExists) = await ResourceExists("service", service.Metadata.Name, service.Metadata.Namespace, true);
                if (serviceExists)
                {
                    service = TryParse<ServiceV1>(output);
                    if (service?.Spec?.Type?.Equals("LoadBalancer", StringComparison.OrdinalIgnoreCase) == true && service?.Status?.LoadBalancer?.Ingress?.Any() == true)
                    {
                        return service.Status.LoadBalancer.Ingress.First().Ip;
                    }
                    else if (service?.Spec?.Type?.Equals("LoadBalancer", StringComparison.OrdinalIgnoreCase) == false && !string.IsNullOrEmpty(service?.Spec?.ClusterIp))
                    {
                        return service.Spec.ClusterIp;
                    }
                }

                ColoredConsole.WriteLine(AdditionalInfoColor($"Waiting for the service to be ready: {service.Metadata.Name}"));
                await Task.Delay(5000);
            }

            return string.Empty;
        }

        private static IDictionary<string, string> GetFunctionKeys(IDictionary<string, string> currentImageFuncKeys, IDictionary<string, string> existingFuncKeys)
        {
            if ((currentImageFuncKeys == null || !currentImageFuncKeys.Any())
                || (existingFuncKeys == null || !existingFuncKeys.Any()))
            {
                return currentImageFuncKeys;
            }

            //The function keys that doesn't exist in Kubernetes yet
            IDictionary<string, string> funcKeys = currentImageFuncKeys.Except(existingFuncKeys, new KeyBasedDictionaryComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

             //Merge the new keys with the keys that already exist in kubernetes
            foreach (var commonKey in existingFuncKeys.Intersect(currentImageFuncKeys, new KeyBasedDictionaryComparer()))
            {
                funcKeys.Add(commonKey);
            }

            return funcKeys;
        }

        public class QuoteNumbersEventEmitter : ChainedEventEmitter
        {
            public QuoteNumbersEventEmitter(IEventEmitter nextEmitter)
                : base(nextEmitter)
            { }

            public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
            {
                if (eventInfo.Source.Type == typeof(string) && double.TryParse(eventInfo.Source.Value.ToString(), out _))
                {
                    eventInfo.Style = ScalarStyle.DoubleQuoted;
                }
                base.Emit(eventInfo, emitter);
            }
        }
        internal static string SerializeResources(IEnumerable<IKubernetesResource> resources, OutputSerializationOptions outputFormat)
        {
            var sb = new StringBuilder();
            foreach (var resource in resources)
            {
                if (outputFormat == OutputSerializationOptions.Json)
                {
                    sb.AppendLine(JsonConvert.SerializeObject(resource, Formatting.Indented));
                }
                else
                {
                    var yaml = new SerializerBuilder()
                        .DisableAliases()
                        .WithNamingConvention(new CamelCaseNamingConvention())
                        .WithEventEmitter(e => new QuoteNumbersEventEmitter(e))
                        .Build();
                    var writer = new StringWriter();
                    yaml.Serialize(writer, resource);
                    sb.AppendLine(writer.ToString().Trim());
                    sb.AppendLine("---");
                }
            }
            return sb.ToString();
        }

        private static DeploymentV1Apps GetDeployment(string name, string @namespace, string image, string pullSecret, int replicaCount, IDictionary<string, string> additionalEnv = null, IDictionary<string, string> annotations = null, int port = -1)
        {
            var deployment = new DeploymentV1Apps
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",

                Metadata = new ObjectMetadataV1
                {
                    Namespace = @namespace,
                    Name = name,
                    Labels = new Dictionary<string, string>
                    {
                        { "app", name }
                    },
                    Annotations = annotations
                },
                Spec = new DeploymentSpecV1Apps
                {
                    Replicas = replicaCount,
                    Selector = new SelectorV1
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            { "app", name }
                        }
                    },
                    Template = new PodTemplateV1
                    {
                        Metadata = new ObjectMetadataV1
                        {
                            Labels = new Dictionary<string, string>
                            {
                                { "app", name }
                            }
                        },
                        Spec = new PodTemplateSpecV1
                        {
                            Containers = new ContainerV1[]
                            {
                              new ContainerV1
                              {
                                Name = name,
                                Image = image,
                                Env = additionalEnv == null
                                    ? null
                                    : additionalEnv.Select(kv => new ContainerEnvironmentV1 { Name = kv.Key, Value = kv.Value }),
                                Ports = port == -1
                                    ? null
                                    : new ContainerPortV1[]
                                    {
                                        new ContainerPortV1
                                        {
                                            ContainerPort = 80
                                        }
                                    },
                                ReadinessProbe = new Probe
                                {
                                    FailureThreshold = 3,
                                    HttpGet = new HttpAction
                                    {
                                        Path = "/",
                                        port = 80,
                                        Scheme = "HTTP"
                                    },
                                    PeriodSeconds = 10,
                                    SuccessThreshold = 1,
                                    TimeoutSeconds = 240
                                },
                                StartupProbe = new Probe
                                {
                                    FailureThreshold = 3,
                                    HttpGet = new HttpAction
                                    {
                                        Path = "/",
                                        port = 80,
                                        Scheme = "HTTP"
                                    },
                                    PeriodSeconds = 10,
                                    SuccessThreshold = 1,
                                    TimeoutSeconds = 240
                                }
                              }
                            },
                            ImagePullSecrets = string.IsNullOrEmpty(pullSecret)
                                ? null
                                : new ImagePullSecretV1[]
                                {
                                    new ImagePullSecretV1
                                    {
                                        Name = pullSecret
                                    }
                                }
                        }
                    }
                }
            };

            return deployment;
        }

        private static ServiceV1 GetService(string name, string @namespace, DeploymentV1Apps deployment, string serviceType, IDictionary<string, string> annotations = null)
        {
            return new ServiceV1
            {
                ApiVersion = "v1",
                Kind = "Service",
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace,
                    Annotations = annotations
                },
                Spec = new ServiceSpecV1
                {
                    Type = serviceType,
                    Ports = new ServicePortV1[]
                    {
                        new ServicePortV1
                        {
                            Port = 80,
                            Protocol = "TCP",
                            TargetPort = 80
                        }
                    },
                    Selector = deployment.Spec.Selector.MatchLabels
                }
            };
        }

        private static SecretsV1 GetSecret(string name, string @namespace, IDictionary<string, string> secrets, IDictionary<string, string> annotations = null)
        {
            return new SecretsV1
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace,
                    Annotations = annotations
                },
                Data = secrets.ToDictionary(k => k.Key, v => Convert.ToBase64String(Encoding.UTF8.GetBytes(v.Value)))
            };
        }

        private static ConfigMapV1 GetConfigMap(string name, string @namespace, IDictionary<string, string> secrets)
        {
            return new ConfigMapV1
            {
                ApiVersion = "v1",
                Kind = "ConfigMap",
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace
                },
                Data = secrets
            };
        }

        public static ServiceAccountV1 GetServiceAccount(string name, string @namespace)
        {
            return new ServiceAccountV1
            {
                ApiVersion = "v1",
                Kind = "ServiceAccount",
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace
                }
            };
        }

        public static RoleV1 GetSecretManagerRole(string name, string @namespace)
        {
            return new RoleV1
            {
                ApiVersion = "rbac.authorization.k8s.io/v1",
                Kind = "Role",
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace
                },
                Rules = new RuleV1[]
                {
                    new RuleV1
                    {
                        ApiGroups = new string[]{""},
                        Resources = new string[]{"secrets", "configMaps"},
                        Verbs = new string[]{ "get", "list", "watch", "create", "update", "patch", "delete" }
                    }
                }
            };
        }

        public static RoleBindingV1 GetRoleBinding(string name, string @namespace, string refRoleName, string subjectName)
        {
            return new RoleBindingV1
            {
                ApiVersion = "rbac.authorization.k8s.io/v1",
                Kind = "RoleBinding",
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace
                },
                RoleRef = new RoleSubjectV1
                {
                    ApiGroup = "rbac.authorization.k8s.io",
                    Kind = "Role",
                    Name = refRoleName
                },
                Subjects = new RoleSubjectV1[]
                {
                    new RoleSubjectV1
                    {
                        Kind = "ServiceAccount",
                        Name = subjectName
                    }
                }
            };
        }

        private static T TryParse<T>(string jsonData)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(jsonData);
            }
            catch
            {
                return default;
            }
        }
    }
}
