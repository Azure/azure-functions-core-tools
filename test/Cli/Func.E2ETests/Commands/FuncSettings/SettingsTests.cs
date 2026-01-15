// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncSettings
{
    /// <summary>
    /// Tests for func settings commands using pre-built test app to avoid
    /// running func init for each test.
    /// </summary>
    public class SettingsTests(ITestOutputHelper log, PreBuiltDotnetIsolatedFixture fixture)
        : IClassFixture<PreBuiltDotnetIsolatedFixture>
    {
        private readonly ITestOutputHelper _log = log;
        private readonly PreBuiltDotnetIsolatedFixture _fixture = fixture;

        [Fact]
        public void AddSetting_PlainText_WritesExpectedJson()
        {
            // Create a unique subdirectory for this test to avoid conflicts
            var workingDir = Path.Combine(_fixture.WorkingDirectory, nameof(AddSetting_PlainText_WritesExpectedJson));
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, workingDir);

            var testName = nameof(AddSetting_PlainText_WritesExpectedJson);
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { "\"IsEncrypted\": false", "\"testKey\": \"valueValue\"" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Add setting (no need for func init - using pre-built app)
            var funcSettingsResult = new FuncSettingsCommand(_fixture.FuncPath, testName, _log)
                                    .WithWorkingDirectory(workingDir)
                                    .Execute(["add", "testKey", "valueValue"]);

            // Validate the result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task AddSetting_Encrypted_And_Decrypt_WorksAsExpected()
        {
            // Create a unique subdirectory for this test
            var workingDir = Path.Combine(_fixture.WorkingDirectory, nameof(AddSetting_Encrypted_And_Decrypt_WorksAsExpected));
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, workingDir);

            var testName = nameof(AddSetting_Encrypted_And_Decrypt_WorksAsExpected);
            var settingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedencryptcontent = new[] { "\"IsEncrypted\": true", "\"testKey\":" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (settingsPath, expectedencryptcontent)
            };

            // Encrypt settings with retry helper
            await FunctionAppSetupHelper.FuncSettingsWithRetryAsync(_fixture.FuncPath, testName, workingDir, _log, ["encrypt"]);

            // Add setting
            var funcSettingsResult = new FuncSettingsCommand(_fixture.FuncPath, testName, _log)
                                    .WithWorkingDirectory(workingDir)
                                    .Execute(["add", "testKey", "valueValue"]);

            // validate the encrypt result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().FilesExistsWithExpectContent(filesToValidate);

            // Decrypt settings
            funcSettingsResult = new FuncSettingsCommand(_fixture.FuncPath, testName, _log)
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
            // Create a unique subdirectory for this test
            var workingDir = Path.Combine(_fixture.WorkingDirectory, nameof(DeleteSetting_RemovesKeyFromSettings_AsExpected));
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, workingDir);

            var testName = nameof(DeleteSetting_RemovesKeyFromSettings_AsExpected);
            var settingsPath = Path.Combine(workingDir, "local.settings.json");
            var unexpectedContent = new[] { "\"testKey\": \"valueValue\"" };

            // Add a setting with retry helper (no func init needed)
            await FunctionAppSetupHelper.FuncSettingsWithRetryAsync(_fixture.FuncPath, testName, workingDir, _log, ["add", "testKey", "valueValue"]);

            // Delete the setting
            var funcSettingsResult = new FuncSettingsCommand(_fixture.FuncPath, testName, _log)
                                        .WithWorkingDirectory(workingDir)
                                        .Execute(["delete", "testKey"]);

            // validate the result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().FileDoesNotContain(settingsPath, unexpectedContent);
        }

        [Fact]
        public async Task ListSettings_DisplaysMaskValuesByDefault()
        {
            // Create a unique subdirectory for this test
            var workingDir = Path.Combine(_fixture.WorkingDirectory, nameof(ListSettings_DisplaysMaskValuesByDefault));
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, workingDir);

            var testName = nameof(ListSettings_DisplaysMaskValuesByDefault);

            // Add a setting with retry helper (no func init needed)
            await FunctionAppSetupHelper.FuncSettingsWithRetryAsync(_fixture.FuncPath, testName, workingDir, _log, ["add", "testkey", "valvalue"]);

            // List settings
            var funcSettingsResult = new FuncSettingsCommand(_fixture.FuncPath, testName, _log)
                                        .WithWorkingDirectory(workingDir)
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
            // Create a unique subdirectory for this test
            var workingDir = Path.Combine(_fixture.WorkingDirectory, nameof(ListSettings_WithShowValue_ShowsActualValues));
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, workingDir);

            var testName = nameof(ListSettings_WithShowValue_ShowsActualValues);

            // Add a setting with retry helper (no func init needed)
            await FunctionAppSetupHelper.FuncSettingsWithRetryAsync(_fixture.FuncPath, testName, workingDir, _log, ["add", "testkey", "valvalue"]);

            // List settings with --showValue option
            var funcSettingsResult = new FuncSettingsCommand(_fixture.FuncPath, testName, _log)
                                        .WithWorkingDirectory(workingDir)
                                        .Execute(["list", "--showValue"]);

            // validate the result
            funcSettingsResult.Should().ExitWith(0);
            funcSettingsResult.Should().HaveStdOutContaining("App Settings:");
            funcSettingsResult.Should().HaveStdOutContaining("Name: testkey");
            funcSettingsResult.Should().HaveStdOutContaining("Value: valvalue");
        }
    }
}
