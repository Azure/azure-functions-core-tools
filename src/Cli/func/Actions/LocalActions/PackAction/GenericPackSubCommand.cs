// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack generic", CommandType = CommandType.SubCommand, ShowInHelp = true, HelpText = "Internal generic runtime pack command")]
    internal class GenericPackSubCommand : PackSubCommandBase
    {
        public GenericPackSubCommand(ISecretsManager secretsManager, PackAction parentAction)
            : base(secretsManager, parentAction)
        {
        }

        protected override void SetupParser()
        {
            // Generic doesn't have any runtime-specific arguments beyond the common ones
        }

        public override async Task RunAsync()
        {
            var functionAppRoot = ParentAction.ResolveFunctionAppRoot();
            ParentAction.ValidateFunctionAppRoot(functionAppRoot);

            var outputPath = ParentAction.ResolveOutputPath(functionAppRoot);
            ParentAction.CleanupExistingPackage(outputPath);

            if (!ParentAction.NoBuild)
            {
                var installExtensionAction = new InstallExtensionAction(SecretsManager, false);
                await installExtensionAction.RunAsync();
            }

            await ParentAction.CreatePackage(functionAppRoot, outputPath);
        }
    }
}
