using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "delete-history", Context = Context.Durable, HelpText = "Clears out the history and instance stores")]
    class DurableDeleteHistory : BaseAction
    {
        public string Version { get; set; }


        private readonly ISecretsManager _secretsManager;
        private readonly DurableManager durableManager;

        public DurableDeleteHistory(ISecretsManager secretsManager)
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

            return parser.Parse(args);
        }

        public override async Task RunAsync()
        {
            await durableManager.DeleteHistory();
        }
    }
}