using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "status", Context = Context.Durable, HelpText = "Checks the status of a specified orchestration instance")]
    class DurableRuntimeStatus : BaseAction
    {
        public string Version { get; set; }

        public string Instance { get; set; }

        public bool AllExecutions { get; set; }

        private readonly ISecretsManager _secretsManager;
        private readonly DurableManager durableManager;

        public DurableRuntimeStatus(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
            durableManager = new DurableManager(secretsManager);
        }


        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            var parser = new FluentCommandLineParser();
            parser
                .Setup<string>("version")
                .WithDescription("This shows up in the help next to the version option.")
                .SetDefault(string.Empty)
                .Callback(v => Version = v);

            parser
                .Setup<string>("instance")
                .WithDescription("This specifies the instanceId of a new orchestration")
                .SetDefault(null)
                .Callback(i => Instance = i);

            parser
                 .Setup<bool>("all-executions")
                 .WithDescription("This specifies the name of an event to raise")
                 .SetDefault(false)
                 .Callback(e => AllExecutions = e);


            return parser.Parse(args);
        }

        public override async Task RunAsync()
        {
            await durableManager.GetOrchestrationState(Instance, AllExecutions);
        }
    }
}