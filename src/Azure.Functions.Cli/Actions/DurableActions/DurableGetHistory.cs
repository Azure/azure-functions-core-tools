using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "history", Context = Context.Durable, HelpText = "Gets the history of the instance of a specified orchestration instance")]
    class DurableGetHistory : BaseAction
    {
        public string Version { get; set; }

        public string Instance { get; set; }


        private readonly ISecretsManager _secretsManager;
        private readonly DurableManager durableManager;

        public DurableGetHistory(ISecretsManager secretsManager)
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
                .WithDescription("This specifies the id of an orchestration")
                .SetDefault(null)
                .Callback(i => Instance = i);

            return parser.Parse(args);
        }

        public override async Task RunAsync()
        {
            await durableManager.GetHistory(Instance);
        }
    }
}