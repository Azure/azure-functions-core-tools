using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;
using System.Linq;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "start-new", Context = Context.Durable, HelpText = "Starts a new instance of a specified orchestratior function")]
    class DurableStartNew : BaseAction
    {
        public string Version { get; set; }

        public string FunctionName { get; set; }

        public string Instance { get; set; }

        public object Input { get; set; }


        private readonly ISecretsManager _secretsManager;
        private readonly DurableManager durableManager;

        public DurableStartNew(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
            durableManager = new DurableManager(secretsManager);
        }


        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            var parser = new FluentCommandLineParser();
            parser
                .Setup<string>("version")
                .WithDescription("This shows up in the help next to the version option")
                .SetDefault(string.Empty)
                .Callback(v => Version = v);

           parser
                .Setup<string>("function-name")
                .WithDescription("This is the name of the orchestrator function for the new instance")
                .SetDefault(null)
                .Callback(n => FunctionName = n);

            parser
                .Setup<string>("instance")
                .WithDescription("This specifies the id of a new orchestration instance")
                .SetDefault(Guid.NewGuid().ToString("N"))
                .Callback(i => Instance = i);

            parser
                .Setup<string>("input")
                .WithDescription("This is the orchestrator function's input object")
                .SetDefault(null)
                .Callback(p => Input = p);

            return parser.Parse(args);
        }

        public override async Task RunAsync()
        {
            await durableManager.StartNew(FunctionName, Version, Instance, Input);
        }
    }
}