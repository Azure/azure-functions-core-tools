﻿﻿using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Colors.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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
                    funcAppKeys[$"{FunctionKeyPrefix}{funcName.ToLower()}.{FunctionDefaultKeyName}"] = GenerateKey();
                }
            }

            return funcAppKeys;
        }

        public static IDictionary<string, string> FuncKeysKubernetesEnvironVariables(string keysSecretCollectionName, bool mountKeysAsContainerVolume)
        {
            var funcKeysKubernetesEnvironVariables = new Dictionary<string, string>
            {
                { AzureWebJobsSecretStorageTypeEnvVariableName, "kubernetes" }
            };

            //if keys needs are not to be mounted as container volume then add "AzureWebJobsKubernetesSecretName" enviornment varibale to the container 
            if (!mountKeysAsContainerVolume)
            {
                funcKeysKubernetesEnvironVariables.Add(AzureWebJobsKubernetesSecretNameEnvVariableName, $"secrets/{keysSecretCollectionName}");
            }

            return funcKeysKubernetesEnvironVariables;
        }

        public static void CreateFuncAppKeysVolumeMountDeploymentResource(IEnumerable<DeploymentV1Apps> deployments, string funcAppKeysSecretsCollectionName)
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

            //Mount the app keys as volume mount to the container at the path "/run/secrets/functions-keys"
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
