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

        private string Id { get; set; }

        private readonly IDurableManager _durableManager;

        public DurableStartNew(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("id")
                .WithDescription("Specifies the id of an orchestration instance")
                .SetDefault($"{Guid.NewGuid():N}")
                .Callback(i => Id = i);
            Parser
                .Setup<string>("function-name")
                .WithDescription("Name of the orchestrator function to start")
                .Required()
                .Callback(n => FunctionName = n);
            Parser
               .Setup<string>("input")
               .WithDescription("Input to the orchestrator function, either in-line or via a JSON file. For files, prefix the path to the file with @ (e.g. \"@path/to/file.json\").")
               .SetDefault(null)
               .Callback(p => Input = p);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            object input = DurableManager.RetrieveCommandInputData(Input);
            await _durableManager.StartNew(ConnectionString, TaskHubName, FunctionName, Id, input);
        }
    }
}