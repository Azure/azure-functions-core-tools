using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "get-history", Context = Context.Durable, HelpText = "Retrieve the history of the specified orchestration instance")]
    class DurableGetHistory : BaseDurableAction
    {
        private readonly IDurableManager _durableManager;

        public DurableGetHistory(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrEmpty(ID))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to retrieve the history for.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to retrieve the history of." });
            }

            await _durableManager.GetHistory(ID);
        }
    }
}