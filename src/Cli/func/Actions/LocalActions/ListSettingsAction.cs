// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "list", Context = Context.Settings, HelpText = "List local settings")]
    internal class ListSettingsAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public ListSettingsAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public bool ShowValues { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>('a', "showValue")
                .Callback(a => ShowValues = a)
                .WithDescription("Specifying this shows decrypted settings.");
            return base.ParseArgs(args);
        }

        public override Task RunAsync()
        {
            ColoredConsole.WriteLine(TitleColor("App Settings:"));
            foreach (var pair in _secretsManager.GetSecrets())
            {
                ColoredConsole
                    .WriteLine($"   -> {TitleColor("Name")}: {pair.Key}")
                    .WriteLine($"      {TitleColor("Value")}: {(ShowValues ? pair.Value : "*****")}")
                    .WriteLine();
            }

            ColoredConsole.WriteLine(TitleColor("Connection Strings:"));
            foreach (var connectionString in _secretsManager.GetConnectionStrings())
            {
                ColoredConsole
                    .WriteLine($"   -> {TitleColor("Name")}: {connectionString.Name}")
                    .WriteLine($"      {TitleColor("Value")}: {(ShowValues ? connectionString.Value : "*****")}")
                    .WriteLine($"      {TitleColor("ProviderName")}: {connectionString.ProviderName}")
                    .WriteLine();
            }

            return Task.CompletedTask;
        }
    }
}
