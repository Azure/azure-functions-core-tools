using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "delete-history", Context = Context.Durable, HelpText = "Delete the history and instance stores")]
    class DurableDeleteHistory : BaseAction
    {
        private readonly IDurableManager _durableManager;

        public DurableDeleteHistory(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override async Task RunAsync()
        {
            await _durableManager.DeleteHistory();
        }
    }
}