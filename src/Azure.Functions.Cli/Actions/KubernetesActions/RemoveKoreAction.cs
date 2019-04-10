using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Kubernetes;
using Fclp;

namespace Azure.Functions.Cli.Actions.KubernetesActions
{
    [Action(Name = "remove", Context = Context.Kubernetes, HelpText = "Remove Kore (non-http scale to zero) and Osiris (http scale to zero) from the kubernetes")]
    internal class RemoveKoreAction : BaseAction
    {
        public string Namespace { get; private set; } = "default";

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            SetFlag<string>("namespace", "Kubernetes namespace to remove kore from to. Default: default", s => Namespace = s);
            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
            await KubernetesHelper.RemoveKore(Namespace);
        }
    }
}