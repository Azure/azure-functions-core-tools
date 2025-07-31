// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncSettings
{
    public class SettingsTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public async Task AddSetting_PlainText_WritesExpectedJson()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(AddSetting_PlainText_WritesExpectedJson);
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { "\"IsEncrypted\": false", "\"testKey\": \"valueValue\"" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Initialize function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated"]);

            // Add setting
            var funcSettingsResult = new FuncSettingsCommand(FuncPath, testName, Log)
                                    .WithWorkingDirectory(workingDir)
                                    .Execute(["add", "testKey", "valueValue"]);

            // Validate the result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task AddSetting_Encrypted_And_Decrypt_WorksAsExpected()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(AddSetting_Encrypted_And_Decrypt_WorksAsExpected);
            var settingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedencryptcontent = new[] { "\"IsEncrypted\": true", "\"testKey\":" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (settingsPath, expectedencryptcontent)
            };

            // Initialize function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated"]);

            // Encrypt settings with retry helper
            await FuncSettingsWithRetryAsync(testName, ["encrypt"]);

            // Add setting
            var funcSettingsResult = new FuncSettingsCommand(FuncPath, testName, Log)
                                    .WithWorkingDirectory(workingDir)
                                    .Execute(["add", "testKey", "valueValue"]);

            // validate the encrypt result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().FilesExistsWithExpectContent(filesToValidate);

            // Decrypt settings
            funcSettingsResult = new FuncSettingsCommand(FuncPath, testName, Log)
                                    .WithWorkingDirectory(workingDir)
                                    .Execute(["decrypt"]);

            var expectedDecryptcontent = new[] { "\"IsEncrypted\": false", "\"testKey\": \"valueValue\"" };
            filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (settingsPath, expectedDecryptcontent)
            };

            // Validate the decrypt result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task DeleteSetting_RemovesKeyFromSettings_AsExpected()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(DeleteSetting_RemovesKeyFromSettings_AsExpected);
            var settingsPath = Path.Combine(WorkingDirectory, "local.settings.json");
            var unexpectedContent = new[] { "\"testKey\": \"valueValue\"" };

            // Initialize the function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated"]);

            // Add a setting with retry helper
            await FuncSettingsWithRetryAsync(testName, ["add", "testKey", "valueValue"]);

            // Delete the setting
            var funcSettingsResult = new FuncSettingsCommand(FuncPath, testName, Log)
                                        .WithWorkingDirectory(workingDir)
                                        .Execute(["delete", "testKey"]);

            // validate the result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().FileDoesNotContain(settingsPath, unexpectedContent);
        }

        [Fact]
        public async Task ListSettings_DisplaysMaskValuesByDefault()
        {
            var testName = nameof(ListSettings_DisplaysMaskValuesByDefault);

            // Initialize the function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated"]);

            // Add a setting with retry helper
            await FuncSettingsWithRetryAsync(testName, ["add", "testkey", "valvalue"]);

            // List settings
            var funcSettingsResult = new FuncSettingsCommand(FuncPath, testName, Log)
                                        .WithWorkingDirectory(WorkingDirectory)
                                        .Execute(["list"]);

            // validate the result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().HaveStdOutContaining("App Settings:");
            funcSettingsResult.Should().HaveStdOutContaining("Name: testkey");
            funcSettingsResult.Should().HaveStdOutMatchesRegex("Value:\\s*\\*+"); // Masked value
            funcSettingsResult.Should().NotHaveStdOutContaining("valvalue");
        }

        [Fact]
        public async Task ListSettings_WithShowValue_ShowsActualValues()
        {
            var testName = nameof(ListSettings_WithShowValue_ShowsActualValues);

            // Initialize the function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated"]);

            // Add a setting with retry helper
            await FuncSettingsWithRetryAsync(testName, ["add", "testkey", "valvalue"]);

            // List settings with --showValue option
            var funcSettingsResult = new FuncSettingsCommand(FuncPath, testName, Log)
                                        .WithWorkingDirectory(WorkingDirectory)
                                        .Execute(["list", "--showValue"]);

            // validate the result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().HaveStdOutContaining("App Settings:");
            funcSettingsResult.Should().HaveStdOutContaining("Name: testkey");
            funcSettingsResult.Should().HaveStdOutContaining("Value: valvalue");
        }
    }
}
