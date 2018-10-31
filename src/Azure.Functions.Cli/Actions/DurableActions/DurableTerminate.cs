using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "terminate", Context = Context.Durable, HelpText = "Terminate the specified orchestration instance")]
    class DurableTerminate : BaseDurableAction
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
            if (string.IsNullOrEmpty(ID))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to terminate.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to terminate." });
            }

            await _durableManager.Terminate(ID, Reason);
        }
    }
}