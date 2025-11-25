// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "init powershell", ParentCommandName = "init", ShowInHelp = true, HelpText = "Options specific to PowerShell apps when running func init")]
    internal class InitPowershellSubcommandAction : BaseAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("managed-dependencies")
                .WithDescription("Installs managed dependencies for the PowerShell function app.")
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
