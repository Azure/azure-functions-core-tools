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
    [Action(Name = "install", Context = Context.Kubernetes, HelpText = "Install Kore (non-http scale to zero) and Osiris (http scale to zero) in the kubernetes cluster from kubectl config")]
    internal class DeployKoreAction : BaseAction
    {
        public string Namespace { get; private set; } = "default";
        public bool KoreOnly { get; private set; }
        public bool DryRun { get; private set; }
        // public OutputSerializationOptions OutputFormat { get; private set; } = OutputSerializationOptions.Yaml;

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("namespace", "Kubernetes namespace to deploy to. Default: default", s => Namespace = s);
            SetFlag<bool>("kore-only", "Install Kore only. By default both kore (non-http scale to zero) and osiris (http scale to zero) are installed", f => KoreOnly = f);
            SetFlag<bool>("dry-run", "Show the deployment template", f => DryRun = f);
            // SetFlag<OutputSerializationOptions>("output", "With --dry-run. Prints deployment in json, yaml or helm. Default: yaml", o => OutputFormat = o);
            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
            var resources = KubernetesHelper.GetKoreResources(Namespace);
            if (DryRun)
            {
                ColoredConsole.WriteLine(KubernetesHelper.SerializeResources(resources, OutputSerializationOptions.Yaml));
                if (!KoreOnly)
                {
                    ColoredConsole.WriteLine(KubernetesHelper.GetOsirisResources(Namespace));
                }
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine(KubernetesHelper.SerializeResources(resources, OutputSerializationOptions.Yaml));
                if (!KoreOnly)
                {
                    sb.AppendLine(KubernetesHelper.GetOsirisResources(Namespace));
                }

                if (!await KubernetesHelper.NamespaceExists(Namespace))
                {
                    await KubernetesHelper.CreateNamespace(Namespace);
                }

                await KubectlHelper.KubectlApply(sb.ToString(), showOutput: true, @namespace: Namespace);
            }
        }
    }
}