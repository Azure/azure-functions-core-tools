using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "delete-history", Context = Context.Durable, HelpText = "Clears out the history and instance stores")]
    class DurableDeleteHistory : BaseAction
    {
        private readonly DurableManager _durableManager;

        public DurableDeleteHistory(DurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override async Task RunAsync()
        {
            await _durableManager.DeleteHistory();
        }
    }
}