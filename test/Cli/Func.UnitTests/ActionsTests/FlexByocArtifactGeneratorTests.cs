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
        [InlineData(WorkerRuntime.Python, "3.11", null, "python.3.11")]
        [InlineData(WorkerRuntime.Python, "3.15", null, "python.3.15")]
        [InlineData(WorkerRuntime.Node, "18", Constants.Languages.JavaScript, "node.javascript.18")]
        [InlineData(WorkerRuntime.Node, "26", Constants.Languages.TypeScript, "node.typescript.26")]
        [InlineData(WorkerRuntime.Java, "21", null, "java.21")]
        [InlineData(WorkerRuntime.Java, "25", null, "java.25")]
        [InlineData(WorkerRuntime.Powershell, "7.2", null, "powershell.7.2")]
        [InlineData(WorkerRuntime.Powershell, "7.6", null, "powershell.7.6")]
        public async Task GetTemplateArtifactsAsync_MissingDockerfileIdentifiesResolvedArtifact(
            WorkerRuntime workerRuntime,
            string runtimeVersion,
            string language,
            string expectedTemplateName)
        {
            Func<Task> act = () => FlexByocArtifactGenerator.GetTemplateArtifactsAsync(
                workerRuntime,
                runtimeVersion,
                language);

            var exception = await act.Should().ThrowAsync<CliException>();
            exception.Which.Message.Should().Be(
                $"Flex BYOC template artifact 'Dockerfile.flexByoc.{expectedTemplateName}' was not present.");
            exception.Which.InnerException.Should().NotBeNull();
        }

        [Theory]
        [InlineData(WorkerRuntime.DotnetIsolated, "net7.0", null)]
        [InlineData(WorkerRuntime.DotnetIsolated, "10.5", null)]
        [InlineData(WorkerRuntime.Python, "3.10", null)]
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
