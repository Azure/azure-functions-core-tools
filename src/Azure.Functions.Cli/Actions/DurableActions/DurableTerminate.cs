using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "terminate", Context = Context.Durable, HelpText = "Terminates a specified orchestration instance")]
    class DurableTerminate : BaseDurableAction
    {
        public string Reason { get; set; }

        private readonly DurableManager _durableManager;

        public DurableTerminate(DurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<string>("reason")
                 .WithDescription("This specifies the reason for terminating the orchestration")
                 .SetDefault("Instance termination.")
                 .Callback(r => Reason = r);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.Terminate(Instance, Reason);
        }
    }
}