using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Kubernetes;
using Fclp;

namespace Azure.Functions.Cli.Actions.KubernetesActions
{
    [Action(Name = "remove", Context = Context.Kubernetes, HelpText = "Remove Keda (non-http scale to zero) from the kubernetes")]
    internal class RemoveKedaAction : BaseAction
    {
        public string Namespace { get; private set; } = "default";

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("namespace", "Kubernetes namespace to remove keda from to. Default: default", s => Namespace = s);
            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
            await KubernetesHelper.RemoveKeda(Namespace);
        }
    }
}