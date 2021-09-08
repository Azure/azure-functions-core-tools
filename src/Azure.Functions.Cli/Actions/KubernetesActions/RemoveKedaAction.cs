using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.KEDA;
using Colors.Net;
using Fclp;

namespace Azure.Functions.Cli.Actions.KubernetesActions
{
    [Action(Name = "remove", Context = Context.Kubernetes, HelpText = "Remove KEDA (non-http scale to zero) from the kubernetes")]
    internal class RemoveKedaAction : BaseAction
    {
        public string Namespace { get; private set; } = "default";

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("namespace", "Kubernetes namespace to remove KEDA from to. Default: default", s => Namespace = s);
            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
            var isInstalled = await KedaHelper.IsInstalled(Namespace);
            if (isInstalled == false)
            {
                ColoredConsole.WriteLine("KEDA is not installed");
                return;
            }

            var kedaVersion = await KedaHelper.DetermineCurrentVersion(Namespace);
            ColoredConsole.WriteLine($"KEDA {kedaVersion} is installed");
            var kedaDeploymentYaml = KedaHelper.GetKedaDeploymentYaml(Namespace, kedaVersion);
            await KubectlHelper.KubectlDelete(kedaDeploymentYaml, showOutput: true);
            ColoredConsole.WriteLine($"KEDA {kedaVersion} is removed");
        }
    }
}