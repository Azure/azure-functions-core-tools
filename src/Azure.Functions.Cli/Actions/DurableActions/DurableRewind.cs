using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "rewind", Context = Context.Durable, HelpText = "Rewind the specified orchestration instance")]
    class DurableRewind : BaseDurableAction
    {
        private string Reason { get; set; }

        private readonly IDurableManager _durableManager;

        public DurableRewind(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<string>("reason")
                 .WithDescription("Reason for rewinding the orchestration")
                 .Callback(r => Reason = r);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrEmpty(ID))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to rewind.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to rewind." });
            }

            await _durableManager.Rewind(ID, Reason);
        }
    }
}