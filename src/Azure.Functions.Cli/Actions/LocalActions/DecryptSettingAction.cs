using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "decrypt", Context = Context.Settings, HelpText = "Decrypt the local settings file")]
    class DecryptSettingAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public DecryptSettingAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override Task RunAsync()
        {
            _secretsManager.DecryptSettings();
            return Task.CompletedTask;
        }
    }
}
