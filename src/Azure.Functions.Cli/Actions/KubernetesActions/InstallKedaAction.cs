using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Colors.Net;
using Fclp;
using Newtonsoft.Json;
using YamlDotNet.Serialization;

namespace Azure.Functions.Cli.Actions.KubernetesActions
{
    [Action(Name = "install", Context = Context.Kubernetes, HelpText = "Install Keda (non-http scale to zero) in the kubernetes cluster from kubectl config")]
    internal class DeployKedaAction : BaseAction
    {
        public string Namespace { get; private set; } = "default";
        public bool DryRun { get; private set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("namespace", "Kubernetes namespace to deploy to. Default: default", s => Namespace = s);
            SetFlag<bool>("dry-run", "Show the deployment template", f => DryRun = f);
            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
            if (DryRun)
            {
                ColoredConsole.WriteLine(KubernetesHelper.GetKedaResources(Namespace));
            }
            else
            {
                if (!await KubernetesHelper.NamespaceExists(Namespace))
                {
                    await KubernetesHelper.CreateNamespace(Namespace);
                }

                await KubectlHelper.KubectlApply(KubernetesHelper.GetKedaResources(Namespace), showOutput: true);
            }
        }
    }
}