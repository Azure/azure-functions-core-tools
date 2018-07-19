//using System.Linq;
//using Azure.Functions.Cli.Arm;
//using Azure.Functions.Cli.Common;
//using Fclp;

//namespace Azure.Functions.Cli.Actions.AzureActions
//{
//    abstract class BaseDurableAction : BaseAction
//    {
//        public string FunctionAppName { get; set; }

//        public BaseDurableAction(IArmManager armManager) : base(armManager)
//        { }

//        public override ICommandLineParserResult ParseArgs(string[] args)
//        {
//            if (args.Any() && !args.First().StartsWith("-"))
//            {
//                FunctionAppName = args.First();
//            }
//            else
//            {
//                throw new CliArgumentsException("Must specify functionApp name.", Parser.Parse(args),
//                    new CliArgument { Name = nameof(FunctionAppName), Description = "Function App name" });
//            }

//            return base.ParseArgs(args);
//        }
//    }
//}
