// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Moq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests
{
    public class GlobalCoreToolsSettingsTests
    {
        [Fact]
        public void Init_LogsWarning_ForJavaWorkerRuntime()
        {
            // Arrange
            var secretsManager = new Mock<ISecretsManager>();
            var args = new[] { "--java" };

            var stringWriter = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stringWriter);

            try
            {
                // Act
                GlobalCoreToolsSettings.Init(secretsManager.Object, args);

                // Assert
                var output = stringWriter.ToString();
                Assert.Contains("This action is not supported when using the core tools directly", output);
                Assert.Contains("java", output, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Console.SetOut(originalOut);

                // Reset static state for other tests
                GlobalCoreToolsSettings.SetWorkerRuntime(WorkerRuntime.None);
            }
        }
    }
}
