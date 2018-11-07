using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "delete-task-hub", Context = Context.Durable, HelpText = "Delete all storage artifacts in the durable task hub")]
    class DurableDeleteTaskHub : BaseAction
    {
        private readonly IDurableManager _durableManager;

        private string ConnectionString { get; set; }

        private bool DeleteInstanceStore { get; set; }

        public DurableDeleteTaskHub(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("connection-string")
                .WithDescription("(Optional) Storage connection string to use.")
                .SetDefault(null)
                .Callback(n => ConnectionString = n);
            Parser
                .Setup<bool>("delete-instance-store")
                .WithDescription("If set to false, the instance store will not be deleted. The default value is true.")
                .SetDefault(true)
                .Callback(n => DeleteInstanceStore = n);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.DeleteTaskHub(ConnectionString, DeleteInstanceStore);
        }
    }
}