// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class NodePackSubcommandActionTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public NodePackSubcommandActionTests()
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
        public void ValidateNodeFunctionApp_ValidStructure_PassesValidation()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");
            File.WriteAllText(Path.Combine(_tempDirectory, "package.json"), "{}");
            var action = new NodePackSubcommandAction(null);
            var options = new PackOptions();
            var ex = Record.Exception(() => action.ValidateFunctionApp(_tempDirectory, options));
            Assert.Null(ex);
        }

        [Fact]
        public void ValidateNodeFunctionApp_MissingPackageJson_FailsValidation()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");
            var action = new NodePackSubcommandAction(null);
            var options = new PackOptions();
            var ex = Assert.Throws<CliException>(() => action.ValidateFunctionApp(_tempDirectory, options));
            Assert.Contains("package.json", ex.Message);
        }
    }
}
