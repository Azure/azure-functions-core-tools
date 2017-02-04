﻿using System.Threading.Tasks;
using Colors.Net;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Helpers;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "set-publish-password", Context = Context.Azure, HelpText = "Set source control publishing password for all Function Apps in Azure.")]
    [Action(Name = "set-publish-username", Context = Context.Azure, HelpText = "Set source control publishing username and password for all Function Apps in Azure")]
    class SetPublishPasswordAction : BasePublishUserAction
    {
        private IArmManager _armManager;

        public SetPublishPasswordAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            ColoredConsole.WriteLine($"Enter password for {AdditionalInfoColor($"\"{UserName}\":")}");
            var password = SecurityHelpers.ReadPassword();
            ColoredConsole.Write($"Confirm your password:");
            var confirmPassword = SecurityHelpers.ReadPassword();
            if (confirmPassword != password)
            {
                ColoredConsole.Error.WriteLine(ErrorColor("passwords do not match"));
            }
            else
            {
                await _armManager.UpdateUserAsync(UserName, password);
                ColoredConsole
                    .WriteLine($"Password for {AdditionalInfoColor($"\"{UserName}\"")} has been updated!");
            }
        }
    }
}
