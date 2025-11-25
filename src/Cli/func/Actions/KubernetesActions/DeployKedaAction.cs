// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.KEDA;
using Colors.Net;
using Fclp;

namespace Azure.Functions.Cli.Actions.KubernetesActions
{
    [Action(Name = "install", Context = Context.Kubernetes, HelpText = "Install KEDA (non-http scale to zero) in the kubernetes cluster from kubectl config.", Order = 1)]
    internal class DeployKedaAction : BaseAction
    {
        public string Namespace { get; private set; }

        public KedaVersion KedaVersion { get; private set; } = KedaVersion.V2;

        public bool DryRun { get; private set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("namespace", "Kubernetes namespace to deploy to. Default: Current namespace in Kubernetes config if set, otherwise 'default'", ns => Namespace = ns);
            SetFlag<KedaVersion>("keda-version", $"Defines the version of KEDA to install. Default: {KedaVersion.V2}. Options are: {KedaVersion.V1} or {KedaVersion.V2}", f => KedaVersion = f);
            SetFlag<bool>("dry-run", "Show the deployment template", f => DryRun = f);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            Namespace ??= await KubernetesHelper.GetCurrentNamespaceOrDefault("default");
            var kedaDeploymentYaml = KedaHelper.GetKedaDeploymentYaml(Namespace, KedaVersion);

            if (DryRun)
            {
                ColoredConsole.WriteLine(kedaDeploymentYaml);
            }
            else
            {
                if (!await KubernetesHelper.NamespaceExists(Namespace))
                {
                    await KubernetesHelper.CreateNamespace(Namespace);
                }

                ColoredConsole.WriteLine($"Installing KEDA {KedaVersion} in namespace {Namespace}");

                await KubectlHelper.KubectlApply(kedaDeploymentYaml, showOutput: true);

                ColoredConsole.WriteLine($"KEDA {KedaVersion} is installed in namespace {Namespace}");
            }
        }
    }
}
