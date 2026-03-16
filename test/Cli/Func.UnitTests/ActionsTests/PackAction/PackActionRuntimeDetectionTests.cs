// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;
using FuncPackAction = Azure.Functions.Cli.Actions.LocalActions.PackAction.PackAction;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class PackActionRuntimeDetectionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalDirectory;
        private readonly Mock<ISecretsManager> _mockSecretsManager;

        public PackActionRuntimeDetectionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
            _originalDirectory = Environment.CurrentDirectory;

            // Simulate absent local.settings.json: GetSecrets returns empty dict
            _mockSecretsManager = new Mock<ISecretsManager>();
            _mockSecretsManager
                .Setup(s => s.GetSecrets(It.IsAny<bool>()))
                .Returns(new Dictionary<string, string>());

            // Write host.json so PackHelpers.ValidateFunctionAppRoot passes
            File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");

            // Ensure FUNCTIONS_WORKER_RUNTIME env var is not set
            Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);
        }

        public void Dispose()
        {
            Environment.CurrentDirectory = _originalDirectory;
            Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public async Task RunAsync_NoSettingsAndNoProjectFiles_ThrowsCliExceptionWithActionableMessage()
        {
            // Arrange: _tempDir has only host.json — no runtime signals
            Environment.CurrentDirectory = _tempDir;
            var action = new FuncPackAction(_mockSecretsManager.Object);

            // Act
            Func<Task> act = () => action.RunAsync();

            // Assert
            var ex = await act.Should().ThrowAsync<CliException>();
            ex.Which.Message.Should().Contain("Unable to determine the worker runtime");
            ex.Which.Message.Should().Contain(Constants.FunctionsWorkerRuntime);
            ex.Which.Message.Should().Contain(Constants.LocalSettingsJsonFileName);
        }

        [Fact]
        public async Task RunAsync_WorkerRuntimeSetInEnvVar_DoesNotThrowRuntimeError()
        {
            // Arrange: env var is set — inference path is never reached
            Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, "dotnet-isolated");
            Environment.CurrentDirectory = _tempDir;
            var action = new FuncPackAction(_mockSecretsManager.Object);

            // Act
            Func<Task> act = () => action.RunAsync();

            // Assert: must NOT throw the "Unable to determine" CliException.
            // The action will still fail (no .csproj to build), but that's a different error.
            var ex = await act.Should().ThrowAsync<CliException>();
            ex.Which.Message.Should().NotContain("Unable to determine the worker runtime");
        }
    }
}
