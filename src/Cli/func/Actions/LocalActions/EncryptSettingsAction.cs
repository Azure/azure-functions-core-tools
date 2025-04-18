// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "encrypt", Context = Context.Settings, HelpText = "Encrypt the local settings file")]
    internal class EncryptSettingsAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public EncryptSettingsAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override Task RunAsync()
        {
            _secretsManager.EncryptSettings();
            return Task.CompletedTask;
        }
    }
}
