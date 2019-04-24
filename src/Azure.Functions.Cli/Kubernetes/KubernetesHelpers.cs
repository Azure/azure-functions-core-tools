using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Kubernetes
{
    public static class KubernetesHelper
    {
        const string KedaDeploymentName = "keda-deployment";
        const string KedaClusterRoleBindingName = "keda-cluster-role-binding";
        const string KedaServiceAccountName = "keda-service-account";
        const string KedaSecretName = "keda-docker-auth";
        const string KedaScaledObjectCrdName = "scaledobjects.keda.k8s.io";
        private static readonly IEnumerable<string> _osirisNames = new[]
        {
            "secret/osiris-osiris-edge-endpoints-hijacker-cert",
            "secret/osiris-osiris-edge",
            "secret/osiris-osiris-edge-proxy-injector-cert",
            "serviceaccount/osiris-osiris-edge",
            "clusterrole.rbac.authorization.k8s.io/osiris-osiris-edge",
            "clusterrolebinding.rbac.authorization.k8s.io/osiris-osiris-edge",
            "service/osiris-osiris-edge-endpoints-hijacker",
            "service/osiris-osiris-edge-proxy-injector",
            "deployment.apps/osiris-osiris-edge-activator",
            "deployment.apps/osiris-osiris-edge-endpoints-controller",
            "deployment.apps/osiris-osiris-edge-endpoints-hijacker",
            "deployment.apps/osiris-osiris-edge-proxy-injector",
            "deployment.apps/osiris-osiris-edge-zeroscaler",
            "mutatingwebhookconfiguration.admissionregistration.k8s.io/osiris-osiris-edge-endpoints-hijacker",
            "mutatingwebhookconfiguration.admissionregistration.k8s.io/osiris-osiris-edge-proxy-injector"
        };

        internal static string GetOsirisResources(string @namespace)
        {
            var injectorCaCert = SecurityHelpers.CreateCACertificate("osiris-proxy-injector-ca");
            var hijackerCaCert = SecurityHelpers.CreateCACertificate("osiris-endpoints-hijacker-ca");

            var injectorAltNames = new[]
            {
                $"osiris-osiris-edge-proxy-injector.{@namespace}",
                $"osiris-osiris-edge-proxy-injector.{@namespace}.svc",
                $"osiris-osiris-edge-proxy-injector.{@namespace}.svc.cluster",
                $"osiris-osiris-edge-proxy-injector.{@namespace}.svc.cluster.local",
            };

            var hijackerAltnames = new[]
            {
                $"osiris-osiris-edge-endpoints-hijacker.{@namespace}",
                $"osiris-osiris-edge-endpoints-hijacker.{@namespace}.svc",
                $"osiris-osiris-edge-endpoints-hijacker.{@namespace}.svc.cluster",
                $"osiris-osiris-edge-endpoints-hijacker.{@namespace}.svc.cluster.local",
            };

            var injectorTlsCert = SecurityHelpers.CreateCertificateFromCA(injectorCaCert, "osiris-osiris-edge-proxy-injector", injectorAltNames);
            var hijackerTlsCert = SecurityHelpers.CreateCertificateFromCA(hijackerCaCert, "osiris-osiris-edge-endpoints-hijacker", hijackerAltnames);

            var caCertForEdgePointHijacker = SecurityHelpers.GetPemCert(hijackerCaCert);
            var tlsCertForEdgeEndpointHijacker = SecurityHelpers.GetPemCert(hijackerTlsCert);
            var tlsKeyForEdgeEndpointHijacker = SecurityHelpers.GetPemRsaKey(hijackerTlsCert);

            var caCertForProxyInjector = SecurityHelpers.GetPemCert(injectorCaCert);
            var tlsCertForProxyInjector = SecurityHelpers.GetPemCert(injectorTlsCert);
            var tlsKeyForProxyInjector = SecurityHelpers.GetPemRsaKey(injectorTlsCert);

            return StaticResources
                .OsirisTemplate
                .Result
                .Replace("OSIRIS_NAMESPACE_PLACEHOLDER", @namespace)
                .Replace("TLS_CERT_FOR_EDGE_ENDPOINTS_HIJACKER", tlsCertForEdgeEndpointHijacker)
                .Replace("TLS_KEY_FOR_EDGE_ENDPOINTS_HIJACKER", tlsKeyForEdgeEndpointHijacker)
                .Replace("CA_CERT_FOR_EDGE_ENDPOINT_HIJACKER", caCertForEdgePointHijacker)
                .Replace("TLS_CERT_FOR_EDGE_PROXY_INJECTOR", tlsCertForProxyInjector)
                .Replace("TLS_KEY_FOR_EDGE_PROXY_INJECTOR", tlsKeyForProxyInjector)
                .Replace("CA_CERT_FOR_EDGE_PROXY_INJECTOR", caCertForProxyInjector);
        }

        internal static async Task<bool> NamespaceExists(string @namespace)
        {
            (_, _, var exitCode) = await KubectlHelper.RunKubectl($"get namespace {@namespace}", ignoreError: true, showOutput: false);
            return exitCode == 0;
        }

        internal static Task CreateNamespace(string @namespace)
            => KubectlHelper.RunKubectl($"create namespace {@namespace}", ignoreError: false, showOutput: true);

        internal static IEnumerable<IKubernetesResource> GetKedaResources(string @namespace)
        {
            return new IKubernetesResource[]
            {
                new CustomResourceDefinitionV1Beta1
                {
                    ApiVersion = "apiextensions.k8s.io/v1beta1",
                    Kind = "CustomResourceDefinition",
                    Metadata = new ObjectMetadataV1
                    {
                        Name = KedaScaledObjectCrdName,
                        Labels = new Dictionary<string, string>
                        {
                            { "app", KedaDeploymentName }
                        }
                    },
                    Spec = new CustomResourceDefinitionSpecV1Beta1
                    {
                        Group = "keda.k8s.io",
                        Version = "v1alpha1",
                        Names = new CustomResourceDefinitionNamesV1Beta1
                        {
                            Kind = "ScaledObject",
                            Plural = "scaledobjects",
                        },
                        Scope = "Namespaced"
                    }
                },
                new SecretsV1
                {
                    ApiVersion = "v1",
                    Kind = "Secret",
                    Metadata = new ObjectMetadataV1
                    {
                        Name = KedaSecretName,
                        Namespace = @namespace,
                        Labels = new Dictionary<string, string>
                        {
                            { "app", KedaDeploymentName }
                        }
                    },
                    Data = new Dictionary<string, string>
                    {
                        { ".dockerconfigjson", "eyJhdXRocyI6eyJwcm9qZWN0a29yZS5henVyZWNyLmlvIjp7InVzZXJuYW1lIjoiYjUxNGI2MGMtNjhjYy00ZjEyLWIzNjEtMzg1ODg3OGIyNDc5IiwicGFzc3dvcmQiOiI0alg1dmtQVFNyVVE5NlVCYlUvQjdDUXJCb0p3VDYyV1NzNVdmWnRGYkI4PSIsImF1dGgiOiJZalV4TkdJMk1HTXROamhqWXkwMFpqRXlMV0l6TmpFdE16ZzFPRGczT0dJeU5EYzVPalJxV0RWMmExQlVVM0pWVVRrMlZVSmlWUzlDTjBOUmNrSnZTbmRVTmpKWFUzTTFWMlphZEVaaVFqZzkifX19" }
                    },
                    Type = "kubernetes.io/dockerconfigjson"
                },
                new ServiceAccountV1
                {
                    ApiVersion = "v1",
                    Kind = "ServiceAccount",
                    Metadata = new ObjectMetadataV1
                    {
                        Name = KedaServiceAccountName,
                        Namespace = @namespace,
                        Labels = new Dictionary<string, string>
                        {
                            { "app", KedaDeploymentName }
                        },
                    }
                },
                new ClusterRoleBindingV1
                {
                    ApiVersion = "rbac.authorization.k8s.io/v1",
                    Kind = "ClusterRoleBinding",
                    Metadata = new ObjectMetadataV1
                    {
                        Labels = new Dictionary<string, string>
                        {
                            { "app", KedaDeploymentName }
                        },
                        Name = KedaClusterRoleBindingName,
                        Namespace = @namespace,
                    },
                    RoleRef = new ClusterRoleBindingRoleRefV1
                    {
                        Kind = "ClusterRole",
                        Name = "cluster-admin",
                        ApiGroup = "rbac.authorization.k8s.io"
                    },
                    Subjects = new ClusterRoleBindingSubjectsV1[]
                    {
                        new ClusterRoleBindingSubjectsV1
                        {
                            Kind = "ServiceAccount",
                            Name = KedaServiceAccountName,
                            Namespace = @namespace,
                        }
                    }
                },
                new DeploymentV1Apps
                {
                    ApiVersion = "apps/v1",
                    Kind = "Deployment",
                    Metadata = new ObjectMetadataV1
                    {
                        Labels = new Dictionary<string, string>
                        {
                            { "app", "keda" }
                        },
                        Name = KedaDeploymentName,
                        Namespace = @namespace,
                    },
                    Spec = new DeploymentSpecV1Apps
                    {
                        Replicas = 1,
                        Selector = new SelectorV1
                        {
                            MatchLabels = new Dictionary<string, string>
                            {
                                { "name", KedaDeploymentName },
                                { "instance", $"{KedaDeploymentName}-instance" }
                            }
                        },
                        Template = new PodTemplateV1
                        {
                            Metadata = new ObjectMetadataV1
                            {
                                Labels = new Dictionary<string, string>
                                {
                                    { "name", KedaDeploymentName },
                                    { "instance", $"{KedaDeploymentName}-instance" }
                                }
                            },
                            Spec = new PodTemplateSpecV1
                            {
                                ServiceAccountName = KedaServiceAccountName,
                                Containers = new ContainerV1[]
                                {
                                    new ContainerV1
                                    {
                                    Name = "keda",
                                    Image = "projectkore.azurecr.io/kore:master",
                                    ImagePullPolicy = "Always",
                                    }
                                },
                                ImagePullSecrets = new ImagePullSecretV1[]
                                {
                                    new ImagePullSecretV1
                                    {
                                        Name = KedaSecretName
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        internal static IEnumerable<IKubernetesResource> GetFunctionsDeploymentResources(string name, string imageName, string @namespace, TriggersPayload triggers, IDictionary<string, string> secrets, string pullSecret = null, string secretsCollectionName = null, string configMapName = null, bool useConfigMap = false, int? pollingInterval = null, int? cooldownPeriod = null, string serviceType = "LoadBalancer")
        {
            ScaledObjectV1Alpha1 scaledobject = null;
            var result = new List<IKubernetesResource>();
            var deployments = new List<DeploymentV1Apps>();
            var httpFunctions = triggers.FunctionsJson
                .Where(b => b.Value["bindings"]?.Any(e => e?["type"].ToString().IndexOf("httpTrigger", StringComparison.OrdinalIgnoreCase) != -1) == true);
            var nonHttpFunctions = triggers.FunctionsJson.Where(f => httpFunctions.All(h => h.Key != f.Key));
            if (httpFunctions.Any())
            {
                int position = 0;
                var enabledFunctions = httpFunctions.ToDictionary(k => $"AzureFunctionsJobHost__functions__{position++}", v => v.Key);
                var deployment = GetDeployment(name + "-http", @namespace, imageName, pullSecret, 1, enabledFunctions, new Dictionary<string, string>
                {
                    { "osiris.deislabs.io/enabled", "true" },
                    { "osiris.deislabs.io/minReplicas", "1" }
                }, port: 80);
                deployments.Add(deployment);
                var service = GetService(name + "-http", @namespace, deployment, serviceType, new Dictionary<string, string>
                {
                    { "osiris.deislabs.io/enabled", "true" },
                    { "osiris.deislabs.io/deployment", deployment.Metadata.Name }
                });
                result.Add(service);
            }

            if (nonHttpFunctions.Any())
            {
                int position = 0;
                var enabledFunctions = nonHttpFunctions.ToDictionary(k => $"AzureFunctionsJobHost__functions__{position++}", v => v.Key);
                var deployment = GetDeployment(name, @namespace, imageName, pullSecret, 0, enabledFunctions);
                deployments.Add(deployment);
                scaledobject = GetScaledObject(name, @namespace, triggers, deployment, pollingInterval, cooldownPeriod);
            }

            if (useConfigMap)
            {
                var configMap = GetConfigMap(name, @namespace, secrets);
                result.Insert(0, configMap);
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
                result.Insert(0, secret);
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

            result = result.Concat(deployments).ToList();

            return scaledobject != null
                ? result.Append(scaledobject)
                : result;
        }

        internal static async Task RemoveKeda(string @namespace)
        {
            await KubectlHelper.RunKubectl($"delete deployment.apps/{KedaDeploymentName} --namespace {@namespace}", ignoreError: false, showOutput: true);
            await KubectlHelper.RunKubectl($"delete clusterrolebinding/{KedaClusterRoleBindingName} --namespace {@namespace}", ignoreError: true, showOutput: true);
            await KubectlHelper.RunKubectl($"delete serviceaccount/{KedaServiceAccountName} --namespace {@namespace}", ignoreError: false, showOutput: true);
            await KubectlHelper.RunKubectl($"delete secrets/{KedaSecretName} --namespace {@namespace}", ignoreError: false, showOutput: true);
            await KubectlHelper.RunKubectl($"delete customresourcedefinition/{KedaScaledObjectCrdName}", ignoreError: false, showOutput: true);
            foreach (var name in _osirisNames)
            {
                await KubectlHelper.RunKubectl($"delete {name} --namespace {@namespace}", ignoreError: true, showOutput: true);
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

        private static ScaledObjectV1Alpha1 GetScaledObject(string name, string @namespace, TriggersPayload triggers, DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod)
        {
            return new ScaledObjectV1Alpha1
            {
                ApiVersion = "keda.k8s.io/v1alpha1",
                Kind = "ScaledObject",
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace,
                    Labels = new Dictionary<string, string>
                    {
                        { "deploymaneName" , deployment.Metadata.Name }
                    }
                },
                Spec = new ScaledObjectSpecV1Alpha1
                {
                    ScaleTargetRef = new ScaledObjectScaleTargetRefV1Alpha1
                    {
                        DeploymentName = deployment.Metadata.Name
                    },
                    PollingInterval = pollingInterval,
                    CooldownPeriod = cooldownPeriod,
                    Triggers = triggers
                        .FunctionsJson
                        .Select(kv => kv.Value)
                        .Where(v => v["bindings"] != null)
                        .Select(b => b["bindings"])
                        .SelectMany(i => i)
                        .Where(b => b?["type"] != null)
                        .Where(b => b["type"].ToString().IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) != -1)
                        .Where(b => b["type"].ToString().IndexOf("httpTrigger", StringComparison.OrdinalIgnoreCase) == -1)
                        .Select(t => new ScaledObjectTriggerV1Alpha1
                        {
                            Type = GetKedaTrigger(t["type"]?.ToString()),
                            Metadata = t.ToObject<Dictionary<string, JToken>>()
                                    .Where(i => i.Value.Type == JTokenType.String)
                                    .ToDictionary(k => k.Key, v => v.Value.ToString())
                        })
                }
            };
        }

        private static string GetKedaTrigger(string triggerType)
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

                default:
                    return triggerType;
            }
        }

        private static async Task<bool> HasKeda()
        {
            var kedaEdgeResult = await KubectlHelper.KubectlGet<SearchResultV1<DeploymentV1Apps>>("deployments --selector=app=keda-edge --all-namespaces");
            var kedaResult = await KubectlHelper.KubectlGet<SearchResultV1<DeploymentV1Apps>>("deployments --selector=app=keda --all-namespaces");
            return kedaResult.Items.Any() || kedaEdgeResult.Items.Any();
        }

        private static async Task<bool> HasScaledObjectCrd()
        {
            var crdResult = await KubectlHelper.KubectlGet<SearchResultV1<CustomResourceDefinitionV1Beta1>>("crd");
            return crdResult.Items.Any(i => i.Metadata.Name == "scaledobjects.keda.k8s.io");
        }


        private static SecretsV1 GetSecret(string name, string @namespace, IDictionary<string, string> secrets)
        {
            return new SecretsV1
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = new ObjectMetadataV1
                {
                    Name = name,
                    Namespace = @namespace
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
    }
}