using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "history", Context = Context.Durable, HelpText = "Gets the history of the instance of a specified orchestration instance")]
    class DurableGetHistory : BaseDurableAction
    {
        private readonly DurableManager _durableManager;

        public DurableGetHistory(DurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override async Task RunAsync()
        {
            await _durableManager.GetHistory(Instance);
        }
    }
}