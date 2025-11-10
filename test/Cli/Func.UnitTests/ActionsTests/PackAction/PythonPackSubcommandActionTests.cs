// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class PythonPackSubcommandActionTests
    {
        [Fact]
        public void ValidatePythonProgrammingModel_OnlyV2Model_ReturnsTrue()
        {
            using var temp = TempDir.Create();
            File.WriteAllText(Path.Combine(temp.Path, "function_app.py"), "# V2 model");

            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(temp.Path, out string errorMessage);

            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_OnlyV1Model_ReturnsTrue()
        {
            using var temp = TempDir.Create();
            var functionDir = Path.Combine(temp.Path, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");

            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(temp.Path, out string errorMessage);

            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_MixedModels_ReturnsFalse()
        {
            using var temp = TempDir.Create();
            File.WriteAllText(Path.Combine(temp.Path, "function_app.py"), "# V2 model");
            var functionDir = Path.Combine(temp.Path, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");

            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(temp.Path, out string errorMessage);

            Assert.False(result);
            Assert.Contains("Cannot mix Python V1 and V2 programming models", errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_CustomScriptFileFromLocalSettings_ReturnsTrue()
        {
            using var temp = TempDir.Create();

            var customScriptName = "my_custom_app.py";
            File.WriteAllText(Path.Combine(temp.Path, customScriptName), "# Custom V2 model");

            var localSettings = new JObject
            {
                ["Values"] = new JObject
                {
                    ["PYTHON_SCRIPT_FILE_NAME"] = customScriptName
                }
            };
            File.WriteAllText(Path.Combine(temp.Path, "local.settings.json"), localSettings.ToString());

            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(temp.Path, out string errorMessage);

            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_CustomScriptFileMixedWithV1_FromLocalSettings_ReturnsFalse()
        {
            using var temp = TempDir.Create();

            var customScriptName = "my_custom_app.py";
            File.WriteAllText(Path.Combine(temp.Path, customScriptName), "# Custom V2 model");

            var functionDir = Path.Combine(temp.Path, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");

            var localSettings = new JObject
            {
                ["Values"] = new JObject
                {
                    ["PYTHON_SCRIPT_FILE_NAME"] = customScriptName
                }
            };
            File.WriteAllText(Path.Combine(temp.Path, "local.settings.json"), localSettings.ToString());

            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(temp.Path, out string errorMessage);

            Assert.False(result);
            Assert.Contains("Cannot mix Python V1 and V2 programming models", errorMessage);
            Assert.Contains(customScriptName, errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_NoV1OrV2Files_ReturnsFalse()
        {
            using var temp = TempDir.Create();

            var result = PythonPackSubcommandAction.ValidatePythonProgrammingModel(temp.Path, out string errorMessage);

            Assert.False(result);
            Assert.Contains("Did not find either", errorMessage);
        }

        /// <summary>
        /// Robust per-test temp directory with retrying cleanup to avoid Windows file-lock flakes.
        /// </summary>
        private sealed class TempDir : IDisposable
        {
            private TempDir(string path) => Path = path;

            public string Path { get; }

            public static TempDir Create()
            {
                var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
                Directory.CreateDirectory(root);
                return new TempDir(root);
            }

            public void Dispose()
            {
                // Best-effort cleanup with retries (Windows can delay releasing handles)
                const int maxAttempts = 6;
                var delay = TimeSpan.FromMilliseconds(50);

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(Path))
                        {
                            Directory.Delete(Path, recursive: true);
                        }

                        break;
                    }
                    catch (IOException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(delay);
                        delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    }
                    catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(delay);
                        delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    }
                }
            }
        }
    }
}
