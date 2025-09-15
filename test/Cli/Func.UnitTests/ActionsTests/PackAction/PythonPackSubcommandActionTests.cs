// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class PythonPackSubcommandActionTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public PythonPackSubcommandActionTests()
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
        public void ValidatePythonProgrammingModel_OnlyV2Model_ReturnsTrue()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "function_app.py"), "# V2 model");
            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);
            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_OnlyV1Model_ReturnsTrue()
        {
            var functionDir = Path.Combine(_tempDirectory, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");
            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);
            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_MixedModels_ReturnsFalse()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "function_app.py"), "# V2 model");
            var functionDir = Path.Combine(_tempDirectory, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");
            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);
            Assert.False(result);
            Assert.Contains("Cannot mix Python V1 and V2 programming models", errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_CustomScriptFileFromLocalSettings_ReturnsTrue()
        {
            var customScriptName = "my_custom_app.py";
            File.WriteAllText(Path.Combine(_tempDirectory, customScriptName), "# Custom V2 model");
            var localSettings = new JObject
            {
                ["Values"] = new JObject
                {
                    ["PYTHON_SCRIPT_FILE_NAME"] = customScriptName
                }
            };
            File.WriteAllText(Path.Combine(_tempDirectory, "local.settings.json"), localSettings.ToString());
            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);
            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_CustomScriptFileMixedWithV1_FromLocalSettings_ReturnsFalse()
        {
            var customScriptName = "my_custom_app.py";
            File.WriteAllText(Path.Combine(_tempDirectory, customScriptName), "# Custom V2 model");
            var functionDir = Path.Combine(_tempDirectory, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");
            var localSettings = new JObject
            {
                ["Values"] = new JObject
                {
                    ["PYTHON_SCRIPT_FILE_NAME"] = customScriptName
                }
            };
            File.WriteAllText(Path.Combine(_tempDirectory, "local.settings.json"), localSettings.ToString());
            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);
            Assert.False(result);
            Assert.Contains("Cannot mix Python V1 and V2 programming models", errorMessage);
            Assert.Contains(customScriptName, errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_NoV1OrV2Files_ReturnsFalse()
        {
            // No function_app.py, no function.json in subdirs
            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);
            Assert.False(result);
            Assert.Contains("Did not find either", errorMessage);
        }
    }
}
