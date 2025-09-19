// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class PowershellPackSubcommandActionTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public PowershellPackSubcommandActionTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void ValidatePowershellFunctionApp_ValidStructure_PassesValidation()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");
            var action = new PowershellPackSubcommandAction();
            var options = new PackOptions();
            var ex = Record.Exception(() => action.ValidateFunctionApp(_tempDirectory, options));
            Assert.Null(ex);
        }
    }
}
