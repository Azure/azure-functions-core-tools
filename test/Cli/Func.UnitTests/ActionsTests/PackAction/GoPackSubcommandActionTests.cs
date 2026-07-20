// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
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
            var validate = () => action.ValidateFunctionApp(_tempDirectory, new PackOptions());

            validate.Should().NotThrow();
        }

        [Fact]
        public void ValidateFunctionApp_MissingHostJson_DoesNotThrow()
        {
            // host.json is optional. Go pack should still validate on go.mod alone.
            File.WriteAllText(Path.Combine(_tempDirectory, "go.mod"), "module example.com/test\n\ngo 1.24\n");

            var action = new GoPackSubcommandAction();
            var validate = () => action.ValidateFunctionApp(_tempDirectory, new PackOptions());

            validate.Should().NotThrow();
        }

        [Fact]
        public void ValidateFunctionApp_MissingGoMod_ThrowsCliException()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");

            var action = new GoPackSubcommandAction();
            var validate = () => action.ValidateFunctionApp(_tempDirectory, new PackOptions());

            validate.Should().Throw<CliException>()
                .Which.Message.Should().Contain("go.mod");
        }
    }
}
