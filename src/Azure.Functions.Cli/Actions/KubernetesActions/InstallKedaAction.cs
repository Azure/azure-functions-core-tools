using System.Threading.Tasks;
using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.KEDA;
using Colors.Net;
using Fclp;

namespace Azure.Functions.Cli.Actions.KubernetesActions
{
    [Action(Name = "install", Context = Context.Kubernetes, HelpText = "Install KEDA (non-http scale to zero) in the kubernetes cluster from kubectl config")]
    internal class DeployKedaAction : BaseAction
    {
        public string Namespace { get; private set; } = "default";
        public KedaVersion KedaVersion { get; private set; }
        public bool DryRun { get; private set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("namespace", "Kubernetes namespace to deploy to. Default: default", s => Namespace = s);
            SetFlag<KedaVersion>("keda-version", "Defines the version of KEDA to install", f => KedaVersion = f);
            SetFlag<bool>("dry-run", "Show the deployment template", f => DryRun = f);

            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
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

                await KubectlHelper.KubectlApply(kedaDeploymentYaml, showOutput: true);
            }
        }
    }
}