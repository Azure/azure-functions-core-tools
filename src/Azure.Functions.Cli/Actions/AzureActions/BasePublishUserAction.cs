using System;
using System.Linq;
using Azure.Functions.Cli.Common;
using Fclp;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BasePublishUserAction : BaseAction
    {
        public string UserName { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                UserName = args.First();
            }
            else
            {
                throw new CliArgumentsException("Must specify a username.",
                    new CliArgument { Name = nameof(UserName), Description = "Publishing userName to set or update." });
            }

            return base.ParseArgs(args);
        }
    }
}
