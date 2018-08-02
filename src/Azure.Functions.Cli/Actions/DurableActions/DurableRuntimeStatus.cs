using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "status", Context = Context.Durable, HelpText = "Checks the status of a specified orchestration instance")]
    class DurableRuntimeStatus : BaseDurableAction
    {
        public bool AllExecutions { get; set; }

        private readonly DurableManager _durableManager;

        public DurableRuntimeStatus(DurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<bool>("all-executions")
                 .WithDescription("This specifies the name of an event to raise")
                 .SetDefault(false)
                 .Callback(e => AllExecutions = e);


            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.GetOrchestrationState(Instance, AllExecutions);
        }
    }
}