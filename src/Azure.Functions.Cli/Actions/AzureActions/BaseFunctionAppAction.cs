using System;
using System.Linq;
using Azure.Functions.Cli.Common;
using Fclp;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BaseFunctionAppAction : BaseAction
    {
        public string FunctionAppName { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                FunctionAppName = args.First();
            }
            else
            {
                throw new CliArgumentsException("Must specify functionApp name.", 
                    new CliArgument { Name = nameof(FunctionAppName), Description = "Function App Name" });
            }

            return base.ParseArgs(args);
        }
    }
}
