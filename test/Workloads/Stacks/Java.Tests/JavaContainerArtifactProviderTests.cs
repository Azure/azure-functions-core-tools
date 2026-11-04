// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Containers;

namespace Azure.Functions.Cli.Workloads.Java.Tests;

public class JavaContainerArtifactProviderTests
{
    [Theory]
    [InlineData(null, "25")]
    [InlineData("21", "21")]
    [InlineData("25", "25")]
    public async Task GenerateAsync_RuntimeVersion_GeneratesExpectedArtifacts(string? requestedVersion, string expectedVersion)
    {
        var provider = new JavaContainerArtifactProvider();
        var project = new JavaFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));
        ContainerArtifactRequest request = CreateRequest(project, requestedVersion);

        ContainerArtifactResult result = await provider.GenerateAsync(request, CancellationToken.None);

        result.Artifacts.Select(artifact => artifact.RelativePath).Should().Equal("Dockerfile", "entrypoint.sh", ".dockerignore");
        string dockerfile = result.Artifacts.Single(artifact => artifact.RelativePath == "Dockerfile").Content;
        dockerfile.Should().Contain($"maven:3.9-eclipse-temurin-{expectedVersion}")
            .And.Contain($"mcr.microsoft.com/openjdk/jdk:{expectedVersion}-ubuntu")
            .And.Contain(JavaContainerArtifactProvider.WorkerPackageId)
            .And.Contain(JavaContainerArtifactProvider.WorkerPackageVersion)
            .And.Contain(JavaContainerArtifactProvider.WorkerPackageFeed)
            .And.Contain(JavaContainerArtifactProvider.WorkerPackageContentPath)
            .And.NotContain("{{");
        result.Artifacts.Single(artifact => artifact.RelativePath == "entrypoint.sh").Content.Should().NotContain("\r");
        if (requestedVersion is null)
        {
            ComputeHash(result).Should().Be("e3c5c370396c83824dc15e8d387ab91e9c06f7ea4c51a1402f890c70858bb4c4");
        }
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedVersion_ThrowsProviderException()
    {
        var provider = new JavaContainerArtifactProvider();
        var project = new JavaFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));
        ContainerArtifactRequest request = CreateRequest(project, "17");

        Func<Task> act = () => provider.GenerateAsync(request, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<ContainerArtifactProviderException>()
            .WithMessage("*version '17'*Java*21, 25*");
    }

    [Fact]
    public void Supports_ClaimsOnlyFlexByocJavaProjects()
    {
        var provider = new JavaContainerArtifactProvider();
        var project = new JavaFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));

        provider.Supports(project, ContainerArtifactCapability.FlexByoc).Should().BeTrue();
        provider.Supports(project, ContainerArtifactCapability.Docker).Should().BeFalse();
    }

    private static ContainerArtifactRequest CreateRequest(JavaFunctionsProject project, string? runtimeVersion) => new(
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

    private static string GetProjectPath() => Path.Combine(Path.GetTempPath(), "func-container-provider-tests", "java");
}
