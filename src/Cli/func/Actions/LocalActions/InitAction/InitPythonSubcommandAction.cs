// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "init python", ParentCommandName = "init", ShowInHelp = true, HelpText = "Options specific to Python apps when running func init")]
    internal class InitPythonSubcommandAction : BaseAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('m', "model")
                .WithDescription("Selects the programming model for the function app. Defaults to latest model. Options: v1, v2.")
                .Callback(_ => { });

            Parser
                .Setup<bool>("no-docs")
                .WithDescription("Skip generating the 'Getting Started' documentation files.")
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
