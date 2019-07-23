using System.Linq;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Fclp;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BaseFunctionAppAction : BaseAzureAction
    {
        public string FunctionAppName { get; set; }

        public string Slot { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any() && !args.First().StartsWith("-"))
            {
                FunctionAppName = args.First();
            }
            else
            {
                throw new CliArgumentsException("Must specify functionApp name.", Parser.Parse(args),
                    new CliArgument { Name = nameof(FunctionAppName), Description = "Function App name" });
            }

            Parser
                .Setup<string>("slot")
                .WithDescription("The deployment slot in the function app to use (if configured)")
                .SetDefault(null)
                .Callback(t => Slot = t);

            return base.ParseArgs(args);
        }
    }
}
