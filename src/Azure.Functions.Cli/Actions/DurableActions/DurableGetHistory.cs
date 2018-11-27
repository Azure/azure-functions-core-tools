using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "get-history", Context = Context.Durable, HelpText = "Retrieve the history of the specified orchestration instance")]
    class DurableGetHistory : BaseDurableActionWithId
    {
        private readonly IDurableManager _durableManager;

        public DurableGetHistory(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override async Task RunAsync()
        {
            await _durableManager.GetHistory(ConnectionString, TaskHubName, Id);
        }
    }
}