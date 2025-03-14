using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "terminate", Context = Context.Durable, HelpText = "Terminate the specified orchestration instance")]
    class DurableTerminate : BaseDurableActionWithId
    {
        private string Reason { get; set; }

        private readonly IDurableManager _durableManager;

        public DurableTerminate(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<string>("reason")
                 .WithDescription("Reason for terminating the orchestration")
                 .Callback(r => Reason = r);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.Terminate(ConnectionString, TaskHubName, Id, Reason);
        }
    }
}