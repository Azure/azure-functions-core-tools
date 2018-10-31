using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "start-new", Context = Context.Durable, HelpText = "Start a new instance of the specified orchestrator function")]
    class DurableStartNew : BaseDurableAction
    {
        private string FunctionName { get; set; }

        private string Input { get; set; }

        private string Version { get; set; }

        private readonly IDurableManager _durableManager;

        public DurableStartNew(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("function-name")
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
            if (string.IsNullOrEmpty(FunctionName))
            {
                throw new CliArgumentsException("Must specify the name of of the orchestration function to start.",
                    new CliArgument { Name = "function-name", Description = "Name of the orchestration function to start." });
            }

            await _durableManager.StartNew(FunctionName, Version, ID, Input);
        }
    }
}