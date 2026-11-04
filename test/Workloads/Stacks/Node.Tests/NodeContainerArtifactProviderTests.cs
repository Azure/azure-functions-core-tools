// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Containers;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public class NodeContainerArtifactProviderTests
{
    [Theory]
    [InlineData(null, "26", "JavaScript", "npm install --omit=dev")]
    [InlineData(null, "26", "TypeScript", "npm run build")]
    [InlineData("18", "18", "JavaScript", "npm install --omit=dev")]
    [InlineData("20", "20", "TypeScript", "npm run build")]
    [InlineData("22", "22", "JavaScript", "npm install --omit=dev")]
    [InlineData("24", "24", "TypeScript", "npm run build")]
    [InlineData("26", "26", "JavaScript", "npm install --omit=dev")]
    [InlineData("18", "18", "TypeScript", "npm run build")]
    [InlineData("20", "20", "JavaScript", "npm install --omit=dev")]
    [InlineData("22", "22", "TypeScript", "npm run build")]
    [InlineData("24", "24", "JavaScript", "npm install --omit=dev")]
    [InlineData("26", "26", "TypeScript", "npm run build")]
    public async Task GenerateAsync_RuntimeAndLanguage_GeneratesExpectedArtifacts(
        string? requestedVersion,
        string expectedVersion,
        string language,
        string expectedBuildCommand)
    {
        var provider = new NodeContainerArtifactProvider();
        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()), language);
        ContainerArtifactRequest request = CreateRequest(project, requestedVersion);

        ContainerArtifactResult result = await provider.GenerateAsync(request, CancellationToken.None);

        result.Artifacts.Select(artifact => artifact.RelativePath).Should().Equal("Dockerfile", "entrypoint.sh", ".dockerignore");
        string dockerfile = result.Artifacts.Single(artifact => artifact.RelativePath == "Dockerfile").Content;
        dockerfile.Should().Contain($"node:{expectedVersion}-bookworm-slim")
            .And.Contain(expectedBuildCommand)
            .And.Contain(NodeContainerArtifactProvider.WorkerPackageId)
            .And.Contain(NodeContainerArtifactProvider.WorkerPackageVersion)
            .And.Contain(NodeContainerArtifactProvider.WorkerPackageFeed)
            .And.Contain(NodeContainerArtifactProvider.WorkerPackageContentPath)
            .And.NotContain("{{");
        result.Artifacts.Single(artifact => artifact.RelativePath == "entrypoint.sh").Content.Should().NotContain("\r");
        if (requestedVersion is null)
        {
            string expectedHash = language == "JavaScript"
                ? "b9afa2a0f67dcccaa8a352b2e897365b296c8fe428d1a9f7502675d07acb4759"
                : "962c66434af1cf990364cfea396e5bfd822fe37d2e3422740e6d7b190ff05616";
            ComputeHash(result).Should().Be(expectedHash);
        }
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedVersion_ThrowsProviderException()
    {
        var provider = new NodeContainerArtifactProvider();
        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()), "JavaScript");
        ContainerArtifactRequest request = CreateRequest(project, "16");

        Func<Task> act = () => provider.GenerateAsync(request, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<ContainerArtifactProviderException>()
            .WithMessage("*version '16'*Node.js*18, 20, 22, 24, 26*");
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedLanguage_ThrowsProviderException()
    {
        var provider = new NodeContainerArtifactProvider();
        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()), "CoffeeScript");
        ContainerArtifactRequest request = CreateRequest(project, null);

        Func<Task> act = () => provider.GenerateAsync(request, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<ContainerArtifactProviderException>()
            .WithMessage("*language of 'javascript' or 'typescript'*");
    }

    [Fact]
    public void Supports_ClaimsOnlyFlexByocNodeProjects()
    {
        var provider = new NodeContainerArtifactProvider();
        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()), "JavaScript");

        provider.Supports(project, ContainerArtifactCapability.FlexByoc).Should().BeTrue();
        provider.Supports(project, ContainerArtifactCapability.Docker).Should().BeFalse();
    }

    private static ContainerArtifactRequest CreateRequest(NodeFunctionsProject project, string? runtimeVersion) => new(
        project.WorkingDirectory,
        project,
        ContainerArtifactCapability.FlexByoc,
        projectInitializationSkipped: true,
        new Dictionary<string, string?> { [ContainerArtifactRequest.RuntimeVersionParameterName] = runtimeVersion });

    private static string ComputeHash(ContainerArtifactResult result)
    {
        string content = string.Join('\0', result.Artifacts.Select(artifact => artifact.Content));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    private static string GetProjectPath() => Path.Combine(Path.GetTempPath(), "func-container-provider-tests", "node");
}
