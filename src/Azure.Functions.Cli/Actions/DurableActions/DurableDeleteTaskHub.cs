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

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.DeleteTaskHub(ConnectionString);
        }
    }
}