// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "init dotnet", ParentCommandName = "init", ShowInHelp = true, HelpText = "Options specific to .NET apps when running func init")]
    internal class InitDotnetSubcommandAction : BaseAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('l', "language")
                .WithDescription("The language for the function app. Options: csharp, fsharp.")
                .Callback(_ => { });

            Parser
                .Setup<string>("target-framework")
                .WithDescription($"The target framework for the .NET project. Options: {string.Join(", ", TargetFrameworkHelper.GetSupportedTargetFrameworks())}.")
                .Callback(_ => { });

            Parser
                .Setup<bool>("csx")
                .WithDescription("Use CSX script-style .NET functions.")
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
