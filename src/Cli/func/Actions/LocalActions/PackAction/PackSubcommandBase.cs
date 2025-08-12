// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    internal abstract class PackSubCommandBase
    {
        protected PackSubCommandBase(ISecretsManager secretsManager, PackAction parentAction)
        {
            SecretsManager = secretsManager;
            ParentAction = parentAction;
            Parser = new FluentCommandLineParser();
        }

        protected FluentCommandLineParser Parser { get; }

        protected ISecretsManager SecretsManager { get; }

        protected PackAction ParentAction { get; }

        public async Task ParseAndRunAsync(string[] args)
        {
            SetupParser();
            var result = Parser.Parse(args);

            if (result.HasErrors)
            {
                var errors = string.Join(Environment.NewLine, result.Errors.Select(e => e.ToString()));
                throw new CliException($"Command line parsing errors: {errors}");
            }

            await RunAsync();
        }

        protected abstract void SetupParser();

        public abstract Task RunAsync();
    }
}
