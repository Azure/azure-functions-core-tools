// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "init node", ParentCommandName = "init", ShowInHelp = true, HelpText = "Options specific to Node.js apps when running func init")]
    internal class InitNodeSubcommandAction : BaseAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('l', "language")
                .WithDescription("The language for the function app. Options: typescript, javascript.")
                .Callback(_ => { });

            Parser
                .Setup<string>('m', "model")
                .WithDescription("The programming model for the function app. Defaults to latest. Options: v3, v4.")
                .Callback(_ => { });

            Parser
                .Setup<bool>("skip-npm-install")
                .WithDescription("Skip running 'npm install' after project creation.")
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
