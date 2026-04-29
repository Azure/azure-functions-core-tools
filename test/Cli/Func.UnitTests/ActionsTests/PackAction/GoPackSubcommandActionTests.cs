// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class GoPackSubcommandActionTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public GoPackSubcommandActionTests()
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
        public void ValidateFunctionApp_ValidStructure_PassesValidation()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");
            File.WriteAllText(Path.Combine(_tempDirectory, "go.mod"), "module example.com/test\n\ngo 1.24\n");

            var action = new GoPackSubcommandAction();
            var ex = Record.Exception(() => action.ValidateFunctionApp(_tempDirectory, new PackOptions()));

            Assert.Null(ex);
        }

        [Fact]
        public void ValidateFunctionApp_MissingHostJson_ThrowsCliException()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "go.mod"), "module example.com/test\n\ngo 1.24\n");

            var action = new GoPackSubcommandAction();
            var ex = Record.Exception(() => action.ValidateFunctionApp(_tempDirectory, new PackOptions()));

            Assert.NotNull(ex);
            Assert.IsType<CliException>(ex);
            Assert.Contains("host.json", ex.Message);
        }

        [Fact]
        public void ValidateFunctionApp_MissingGoMod_ThrowsCliException()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");

            var action = new GoPackSubcommandAction();
            var ex = Record.Exception(() => action.ValidateFunctionApp(_tempDirectory, new PackOptions()));

            Assert.NotNull(ex);
            Assert.IsType<CliException>(ex);
            Assert.Contains("go.mod", ex.Message);
        }
    }
}
