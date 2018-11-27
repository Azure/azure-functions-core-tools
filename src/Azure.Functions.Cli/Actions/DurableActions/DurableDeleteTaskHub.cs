using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "delete-task-hub", Context = Context.Durable, HelpText = "Delete all storage artifacts in the durable task hub")]
    class DurableDeleteTaskHub : BaseDurableAction
    {
        private readonly IDurableManager _durableManager;

        public DurableDeleteTaskHub(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override async Task RunAsync()
        {
            await _durableManager.DeleteTaskHub(ConnectionString, TaskHubName);
        }
    }
}