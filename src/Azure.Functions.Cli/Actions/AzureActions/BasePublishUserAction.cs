using System.Linq;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Fclp;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BasePublishUserAction : BaseAzureAction
    {
        public string UserName { get; set; }

        public BasePublishUserAction(IArmManager armManager) : base(armManager)
        { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                UserName = args.First();
            }
            else
            {
                throw new CliArgumentsException("Must specify a username.",
                    new CliArgument { Name = nameof(UserName), Description = "Publishing username to set or update" });
            }

            return base.ParseArgs(args);
        }
    }
}
