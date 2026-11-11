// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Common;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class DotnetPackSubcommandActionTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public DotnetPackSubcommandActionTests()
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
        public void ValidateDotnetFolderStructure_ValidStructure_ReturnsTrue()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");
            File.WriteAllText(Path.Combine(_tempDirectory, "functions.metadata"), "{}");
            Directory.CreateDirectory(Path.Combine(_tempDirectory, ".azurefunctions"));

            var result = DotnetPackSubcommandAction.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out string errorMessage);

            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Theory]
        [InlineData("true")]
        [InlineData(true)]
        public void ValidateDotnetFolderStructure_WorkerIndexedPayload_ReturnsTrue(object workerIndexing)
        {
            WriteWorkerIndexedPayload(workerIndexing);

            var result = DotnetPackSubcommandAction.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out string errorMessage);

            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Theory]
        [InlineData("worker.config.json")]
        [InlineData("extensions.json")]
        [InlineData("App.dll")]
        [InlineData(".azurefunctions/function.deps.json")]
        [InlineData(".azurefunctions")]
        public void ValidateDotnetFolderStructure_IncompleteWorkerIndexedPayload_ReturnsFalse(string missingArtifact)
        {
            WriteWorkerIndexedPayload("true");
            var path = Path.Combine(_tempDirectory, missingArtifact);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }

            Assert.False(DotnetPackSubcommandAction.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out string errorMessage));
            Assert.NotEmpty(errorMessage);
        }

        [Fact]
        public void ValidateDotnetFolderStructure_WorkerIndexedPayload_UsesFileSystemOverride()
        {
            var fileSystem = Substitute.For<IFileSystem>();
            var workerConfigPath = Path.Combine(_tempDirectory, "worker.config.json");
            var extensionsPath = Path.Combine(_tempDirectory, "extensions.json");
            fileSystem.File.Exists(workerConfigPath).Returns(true);
            fileSystem.File.Exists(extensionsPath).Returns(true);
            fileSystem.File.Exists(Path.Combine(_tempDirectory, "App.dll")).Returns(true);
            fileSystem.File.Exists(Path.Combine(_tempDirectory, ".azurefunctions", "function.deps.json")).Returns(true);
            fileSystem.Directory.Exists(Path.Combine(_tempDirectory, ".azurefunctions")).Returns(true);
            fileSystem.File.ReadAllText(workerConfigPath).Returns(
                "{\"description\":{\"language\":\"dotnet-isolated\",\"workerIndexing\":true,\"defaultWorkerPath\":\"App.dll\"}}");
            fileSystem.File.ReadAllText(extensionsPath).Returns("{\"extensions\":[]}");

            using (FileSystemHelpers.Override(fileSystem))
            {
                var result = DotnetPackSubcommandAction.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out string errorMessage);

                Assert.True(result);
                Assert.Empty(errorMessage);
            }
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{")]
        [InlineData("{\"description\": []}")]
        [InlineData("{\"description\":{\"language\":\"dotnet-isolated\",\"workerIndexing\":{},\"defaultWorkerPath\":\"App.dll\"}}")]
        [InlineData("{\"description\":{\"language\":\"dotnet-isolated\",\"workerIndexing\":\"true\",\"defaultWorkerPath\":[]}}")]
        [InlineData("{\"description\":{\"language\":\"dotnet-isolated\",\"workerIndexing\":\"false\",\"defaultWorkerPath\":\"App.dll\"}}")]
        [InlineData("{\"description\":{\"language\":\"node\",\"workerIndexing\":\"true\",\"defaultWorkerPath\":\"App.dll\"}}")]
        [InlineData("{\"description\":{\"language\":\"dotnet-isolated\",\"workerIndexing\":\"true\"}}")]
        [InlineData("{\"description\":{\"language\":\"dotnet-isolated\",\"workerIndexing\":\"true\",\"defaultWorkerPath\":\"../App.dll\"}}")]
        public void ValidateDotnetFolderStructure_InvalidWorkerConfiguration_ReturnsFalse(string configuration)
        {
            WriteWorkerIndexedPayload("true");
            File.WriteAllText(Path.Combine(_tempDirectory, "worker.config.json"), configuration);

            Assert.False(DotnetPackSubcommandAction.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out _));
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{}")]
        public void ValidateDotnetFolderStructure_InvalidExtensionConfiguration_ReturnsFalse(string configuration)
        {
            WriteWorkerIndexedPayload("true");
            File.WriteAllText(Path.Combine(_tempDirectory, "extensions.json"), configuration);

            Assert.False(DotnetPackSubcommandAction.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out _));
        }

        [Theory]
        [InlineData("functions.metadata")]
        [InlineData(".azurefunctions")]
        public void ValidateDotnetFolderStructure_IncompleteLegacyPayload_ReturnsFalse(string missingArtifact)
        {
            if (missingArtifact != "functions.metadata")
            {
                File.WriteAllText(Path.Combine(_tempDirectory, "functions.metadata"), "[]");
            }

            if (missingArtifact != ".azurefunctions")
            {
                Directory.CreateDirectory(Path.Combine(_tempDirectory, ".azurefunctions"));
                File.WriteAllText(Path.Combine(_tempDirectory, ".azurefunctions", "function.deps.json"), "{}");
            }

            Assert.False(DotnetPackSubcommandAction.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out _));
        }

        private void WriteWorkerIndexedPayload(object workerIndexing)
        {
            var configuration = new JObject
            {
                ["description"] = new JObject
                {
                    ["language"] = "dotnet-isolated",
                    ["workerIndexing"] = JToken.FromObject(workerIndexing),
                    ["defaultWorkerPath"] = "App.dll"
                }
            };
            File.WriteAllText(Path.Combine(_tempDirectory, "worker.config.json"), configuration.ToString());
            File.WriteAllText(Path.Combine(_tempDirectory, "extensions.json"), "{\"extensions\":[]}");
            File.WriteAllText(Path.Combine(_tempDirectory, "App.dll"), string.Empty);
            Directory.CreateDirectory(Path.Combine(_tempDirectory, ".azurefunctions"));
            File.WriteAllText(Path.Combine(_tempDirectory, ".azurefunctions", "function.deps.json"), "{}");
        }
    }
}
