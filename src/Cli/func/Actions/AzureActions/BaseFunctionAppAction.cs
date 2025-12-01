// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Fclp;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    internal abstract class BaseFunctionAppAction : BaseAzureAction
    {
        public string FunctionAppName { get; set; }

        public string Slot { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("slot")
                .WithDescription("The deployment slot in the function app to use (if configured)")
                .SetDefault(null)
                .Callback(t => Slot = t);

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FunctionAppName = args.First();
            }
            else if (!GlobalCoreToolsSettings.IsHelpRunning)
            {
                throw new CliArgumentsException(
                    "Must specify functionApp name.",
                    base.ParseArgs(args),
                    new CliArgument { Name = nameof(FunctionAppName), Description = "Function App name" });
            }

            return base.ParseArgs(args);
        }
    }
}
