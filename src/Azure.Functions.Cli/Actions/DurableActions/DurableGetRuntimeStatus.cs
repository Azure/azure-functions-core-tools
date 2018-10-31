using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "get-runtime-status", Context = Context.Durable, HelpText = "Retrieve the status of the specified orchestration instance")]
    class DurableGetRuntimeStatus : BaseDurableAction
    {
        private readonly IDurableManager _durableManager;

        public DurableGetRuntimeStatus(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.GetRuntimeStatus(Id);
        }
    }
}