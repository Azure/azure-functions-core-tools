using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Kubernetes.FuncKeys
{
    public class FuncAppKeysHelper
    {
        private const string FuncAppKeysVolumeName = "functions-keys-volume";
        private const string KubernetesSecretsMountPath = "/run/secrets/functions-keys";
        private const string AzureWebJobsSecretStorageTypeEnvVariableName = "AzureWebJobsSecretStorageType";
        private const string AzureWebJobsKubernetesSecretNameEnvVariableName = "AzureWebJobsKubernetesSecretName";
        private const string MasterKey = "host.master";
        private const string HostFunctionKey = "host.function.default";
        private const string HostSystemKey = "host.systemKey.default";
        private const string FunctionKeyPrefix = "functions.";
        private const string FunctionDefaultKeyName = "default";
        /// <summary>
        /// Implementation of this method creates the Host and Function Keys
        /// </summary>
        /// <param name="functionNames">The <see cref="IEnumerable{string}"></see> of function names </param>
        /// <returns>The <see cref="IDictionary{string, string}"/> of function app's host and function keys</returns>
        public static IDictionary<string, string> CreateKeys(IEnumerable<string> functionNames)
        {
            var funcAppKeys = new Dictionary<string, string>
            {
                { MasterKey, GenerateKey() },
                { HostFunctionKey, GenerateKey() },
                { HostSystemKey, GenerateKey() }
            };

            if (functionNames?.Any() == true)
            {
                foreach (var funcName in functionNames)
                {
                    funcAppKeys[$"{FunctionKeyPrefix}{funcName}.{FunctionDefaultKeyName}"] = GenerateKey();
                }
            }

            return funcAppKeys;
        }

        public static void AddAppKeysEnvironVariableNames(IDictionary<string, string> envVariables,
            string funcAppKeysSecretsCollectionName,
            string funcAppKeysConfigMapName,
            bool mountFuncKeysAsContainerVolume)
        {
            if (envVariables == null)
            {
                envVariables = new Dictionary<string, string>();
            }

            if (!envVariables.ContainsKey(AzureWebJobsSecretStorageTypeEnvVariableName))
            {
                envVariables.Add(AzureWebJobsSecretStorageTypeEnvVariableName, "kubernetes");
            }

            if (!envVariables.ContainsKey(AzureWebJobsKubernetesSecretNameEnvVariableName) 
                && !mountFuncKeysAsContainerVolume)
            {
                if (!string.IsNullOrWhiteSpace(funcAppKeysSecretsCollectionName))
                {
                    envVariables.Add(AzureWebJobsKubernetesSecretNameEnvVariableName, $"secrets/{funcAppKeysSecretsCollectionName}");
                }
                else if (!string.IsNullOrWhiteSpace(funcAppKeysConfigMapName))
                {
                    envVariables.Add(AzureWebJobsKubernetesSecretNameEnvVariableName, $"configmaps/{funcAppKeysConfigMapName}");
                }
            }
        }

        public static void CreateFuncAppKeysVolumeMountDeploymentResource(IEnumerable<DeploymentV1Apps> deployments,
            string funcAppKeysSecretsCollectionName,
            string funcAppKeysConfigMapName)
        {
            if (deployments?.Any() == false)
            {
                return;
            }

            var volume = new VolumeV1
            {
                Name = FuncAppKeysVolumeName
            };

            if (!string.IsNullOrWhiteSpace(funcAppKeysSecretsCollectionName))
            {
                volume.VolumeSecret = new VolumeSecretV1 { SecretName = funcAppKeysSecretsCollectionName };
            }
            else if (!string.IsNullOrWhiteSpace(funcAppKeysConfigMapName))
            {
                volume.VolumeConfigMap = new VolumeConfigMapV1 { Name = funcAppKeysSecretsCollectionName };
            }

            foreach (var deployment in deployments)
            {
                deployment.Spec.Template.Spec.Volumes = new VolumeV1[] { volume };
                deployment.Spec.Template.Spec.Containers.First().VolumeMounts = new ContainerVolumeMountV1[]
                {
                        new ContainerVolumeMountV1
                        {
                            Name = FuncAppKeysVolumeName,
                            MountPath = KubernetesSecretsMountPath
                        }
                };
            }
        }

        private static string GenerateKey()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] data = new byte[40];
                rng.GetBytes(data);
                string secret = Convert.ToBase64String(data);

                // Replace pluses as they are problematic as URL values
                return secret.Replace('+', 'a');
            }
        }
    }
}
