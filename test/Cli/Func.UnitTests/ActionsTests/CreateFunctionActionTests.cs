// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Moq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class CreateFunctionActionTests
    {
        [Fact]
        public async Task UpdateLanguageAndRuntime_ThrowsCliException_ForJavaWorkerRuntime()
        {
            // Arrange
            var templatesManager = new Mock<ITemplatesManager>();
            var secretsManager = new Mock<ISecretsManager>();
            var contextHelpManager = new Mock<IContextHelpManager>();
            var action = new CreateFunctionAction(templatesManager.Object, secretsManager.Object, contextHelpManager.Object)
            {
                Language = "java"
            };

            // Set the current worker runtime to Java, mocking the behavior where func init has already been run
            GlobalCoreToolsSettings.CurrentWorkerRuntime = WorkerRuntime.Java;

            // Ensure local.settings.json exists in the current directory
            var localSettingsPath = Path.Combine(Environment.CurrentDirectory, "local.settings.json");
            File.WriteAllText(localSettingsPath, "{}"); // Write a minimal valid JSON

            // Simulate worker runtime is Java
            typeof(CreateFunctionAction)
                .GetField("_workerRuntime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(action, WorkerRuntime.Java);

            var stringWriter = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stringWriter);

            try
            {
                // make sure no error is thrown
                await action.UpdateLanguageAndRuntime();

                // Assert
                var output = stringWriter.ToString();
                Assert.Contains("This action is not supported", output);
                Assert.Contains("Java", output);
            }
            finally
            {
                Console.SetOut(originalOut);
                if (File.Exists(localSettingsPath))
                {
                    File.Delete(localSettingsPath);
                }
            }
        }
    }
}
