// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Common;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class CustomPackSubcommandActionTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public CustomPackSubcommandActionTests()
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
        public void ValidateCustomHandlerFunctionApp_ValidStructure_PassesValidation()
        {
            var hostJson = new JObject
            {
                ["customHandler"] = new JObject
                {
                    ["description"] = new JObject
                    {
                        ["defaultExecutablePath"] = "TurnThisExecutable"
                    }
                }
            };
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), hostJson.ToString());
            File.WriteAllText(Path.Combine(_tempDirectory, "TurnThisExecutable"), "# executable");
            var action = new CustomPackSubcommandAction();
            var options = new PackOptions();
            var ex = Record.Exception(() => action.ValidateFunctionApp(_tempDirectory, options));
            Assert.Null(ex);
        }

        [Fact]
        public void ValidateCustomHandlerFunctionApp_MissingExecutable_WarnsButDoesNotThrow()
        {
            var hostJson = new JObject
            {
                ["customHandler"] = new JObject
                {
                    ["description"] = new JObject
                    {
                        ["defaultExecutablePath"] = "TurnThisExecutable"
                    }
                }
            };
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), hostJson.ToString());
            var action = new CustomPackSubcommandAction();
            var options = new PackOptions();
            var ex = Record.Exception(() => action.ValidateFunctionApp(_tempDirectory, options));
            Assert.Null(ex); // Should not throw, just warn
        }

        [Fact]
        public void ValidateCustomHandlerFunctionApp_MissingHostJson_Throws()
        {
            var action = new CustomPackSubcommandAction();
            var options = new PackOptions();
            var ex = Assert.Throws<CliException>(() => action.ValidateFunctionApp(_tempDirectory, options));
            Assert.Contains("host.json", ex.Message);
        }
    }
}
