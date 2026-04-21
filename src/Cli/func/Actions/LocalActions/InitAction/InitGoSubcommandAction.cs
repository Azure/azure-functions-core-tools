// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "init go", ParentCommandName = "init", ShowInHelp = true, HelpText = "Options specific to Go runtime apps when running func init")]
    internal class InitGoSubcommandAction : BaseAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("skip-go-mod-tidy")
                .WithDescription("Skip running 'go mod tidy' after Go project creation.")
                .Callback(_ => { });

            return base.ParseArgs(args);
        }

        public override Task RunAsync()
        {
            // This method is never called - the main InitAction handles execution
            return Task.CompletedTask;
        }
    }
}
