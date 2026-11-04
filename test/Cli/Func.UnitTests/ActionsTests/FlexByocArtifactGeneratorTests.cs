// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class FlexByocArtifactGeneratorTests
    {
        [Theory]
        [InlineData(WorkerRuntime.DotnetIsolated, "11")]
        [InlineData(WorkerRuntime.Python, "3.14")]
        [InlineData(WorkerRuntime.Node, "26")]
        [InlineData(WorkerRuntime.Java, "25")]
        [InlineData(WorkerRuntime.Powershell, "7.6")]
        public void ResolveRuntimeVersion_UsesLatestSupportedVersionByDefault(
            WorkerRuntime workerRuntime,
            string expectedVersion)
        {
            FlexByocArtifactGenerator.ResolveRuntimeVersion(workerRuntime, null)
                .Should().Be(expectedVersion);
        }

        [Theory]
        [InlineData("net8.0")]
        [InlineData("9")]
        [InlineData("10.0")]
        [InlineData("net11.0")]
        public async Task GetTemplateArtifactsAsync_LoadsDotnetIsolatedArtifacts(string runtimeVersion)
        {
            FlexByocTemplateArtifacts artifacts = await FlexByocArtifactGenerator.GetTemplateArtifactsAsync(
                WorkerRuntime.DotnetIsolated,
                runtimeVersion,
                null);

            artifacts.Dockerfile.Should().NotBeNullOrWhiteSpace();
            artifacts.Entrypoint.Should().NotBeNullOrWhiteSpace();
            artifacts.DockerIgnore.Should().NotBeNullOrWhiteSpace();
        }

        [Theory]
        [InlineData("3.11")]
        [InlineData("3.12")]
        [InlineData("3.13")]
        [InlineData("3.14")]
        public async Task GetTemplateArtifactsAsync_LoadsPythonArtifacts(string runtimeVersion)
        {
            FlexByocTemplateArtifacts artifacts = await FlexByocArtifactGenerator.GetTemplateArtifactsAsync(
                WorkerRuntime.Python,
                runtimeVersion,
                null);

            artifacts.Dockerfile.Should().NotBeNullOrWhiteSpace();
            artifacts.Entrypoint.Should().NotBeNullOrWhiteSpace();
            artifacts.DockerIgnore.Should().NotBeNullOrWhiteSpace();
        }

        [Theory]
        [InlineData("18", Constants.Languages.JavaScript)]
        [InlineData("20", Constants.Languages.JavaScript)]
        [InlineData("22", Constants.Languages.JavaScript)]
        [InlineData("24", Constants.Languages.JavaScript)]
        [InlineData("26", Constants.Languages.JavaScript)]
        [InlineData("18", Constants.Languages.TypeScript)]
        [InlineData("20", Constants.Languages.TypeScript)]
        [InlineData("22", Constants.Languages.TypeScript)]
        [InlineData("24", Constants.Languages.TypeScript)]
        [InlineData("26", Constants.Languages.TypeScript)]
        public async Task GetTemplateArtifactsAsync_LoadsNodeArtifacts(string runtimeVersion, string language)
        {
            FlexByocTemplateArtifacts artifacts = await FlexByocArtifactGenerator.GetTemplateArtifactsAsync(
                WorkerRuntime.Node,
                runtimeVersion,
                language);

            artifacts.Dockerfile.Should().NotBeNullOrWhiteSpace();
            artifacts.Entrypoint.Should().NotBeNullOrWhiteSpace();
            artifacts.DockerIgnore.Should().NotBeNullOrWhiteSpace();
        }

        [Theory]
        [InlineData("7.2")]
        [InlineData("7.4")]
        [InlineData("7.6")]
        public async Task GetTemplateArtifactsAsync_LoadsPowerShellArtifacts(string runtimeVersion)
        {
            FlexByocTemplateArtifacts artifacts = await FlexByocArtifactGenerator.GetTemplateArtifactsAsync(
                WorkerRuntime.Powershell,
                runtimeVersion,
                null);

            artifacts.Dockerfile.Should().NotBeNullOrWhiteSpace();
            artifacts.Entrypoint.Should().NotBeNullOrWhiteSpace();
            artifacts.DockerIgnore.Should().NotBeNullOrWhiteSpace();
        }

        [Theory]
        [InlineData("21")]
        [InlineData("25")]
        public async Task GetTemplateArtifactsAsync_LoadsJavaArtifacts(string runtimeVersion)
        {
            FlexByocTemplateArtifacts artifacts = await FlexByocArtifactGenerator.GetTemplateArtifactsAsync(
                WorkerRuntime.Java,
                runtimeVersion,
                null);

            artifacts.Dockerfile.Should().NotBeNullOrWhiteSpace();
            artifacts.Entrypoint.Should().NotBeNullOrWhiteSpace();
            artifacts.DockerIgnore.Should().NotBeNullOrWhiteSpace();
        }

        [Theory]
        [InlineData(WorkerRuntime.DotnetIsolated, "net7.0", null)]
        [InlineData(WorkerRuntime.DotnetIsolated, "10.5", null)]
        [InlineData(WorkerRuntime.Python, "3.10", null)]
        [InlineData(WorkerRuntime.Python, "3.15", null)]
        [InlineData(WorkerRuntime.Node, "16", Constants.Languages.JavaScript)]
        [InlineData(WorkerRuntime.Java, "17", null)]
        [InlineData(WorkerRuntime.Powershell, "7.0", null)]
        public async Task GetTemplateArtifactsAsync_RejectsUnsupportedVersion(
            WorkerRuntime workerRuntime,
            string runtimeVersion,
            string language)
        {
            Func<Task> act = () => FlexByocArtifactGenerator.GetTemplateArtifactsAsync(
                workerRuntime,
                runtimeVersion,
                language);

            await act.Should().ThrowAsync<CliException>();
        }
    }
}
