using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "get-runtime-status", Context = Context.Durable, HelpText = "Retrieve the status of the specified orchestration instance")]
    class DurableGetRuntimeStatus : BaseDurableAction
    {
        private readonly IDurableManager _durableManager;

        private bool GetAllExecutions;

        public DurableGetRuntimeStatus(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("get-all-executions")
                .WithDescription("If true, the status of all executions is retrieved. If false, only the status of the most recent execution is retrieved.")
                .SetDefault(false)
                .Callback(n => GetAllExecutions = n);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrEmpty(ID))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to get the runtime status of.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance for which to retrieve the runtime status." });
            }

            await _durableManager.GetRuntimeStatus(ID, GetAllExecutions);
        }
    }
}