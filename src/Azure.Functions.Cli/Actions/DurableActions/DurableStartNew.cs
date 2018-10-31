using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "start-new", Context = Context.Durable, HelpText = "Start a new instance of the specified orchestrator function")]
    class DurableStartNew : BaseDurableAction
    {
        private string FunctionName { get; set; }

        private object Input { get; set; }

        private string Version { get; set; }

        private readonly IDurableManager _durableManager;

        public DurableStartNew(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("functionName")
                .WithDescription("Name of the orchestrator function to start")
                .SetDefault(null)
                .Callback(n => FunctionName = n);
            Parser
               .Setup<string>("input")
               .WithDescription("Input to the orchestrator function")
               .SetDefault(null)
               .Callback(p => Input = p);
            Parser
               .Setup<string>("version")
               .WithDescription("Version of the orchestrator function")
               .SetDefault(null)
               .Callback(v => Version = v);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.StartNew(FunctionName, Version, Id, Input);
        }
    }
}