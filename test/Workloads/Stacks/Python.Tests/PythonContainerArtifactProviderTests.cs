// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Containers;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonContainerArtifactProviderTests
{
    [Theory]
    [InlineData(null, "3.14")]
    [InlineData("3.11", "3.11")]
    [InlineData("3.12", "3.12")]
    [InlineData("3.13", "3.13")]
    [InlineData("3.14", "3.14")]
    public async Task GenerateAsync_RuntimeVersion_GeneratesExpectedArtifacts(string? requestedVersion, string expectedVersion)
    {
        var provider = new PythonContainerArtifactProvider();
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));
        ContainerArtifactRequest request = CreateRequest(project, requestedVersion);

        ContainerArtifactResult result = await provider.GenerateAsync(request, CancellationToken.None);

        result.Artifacts.Select(artifact => artifact.RelativePath).Should().Equal("Dockerfile", "entrypoint.sh", ".dockerignore");
        string dockerfile = result.Artifacts.Single(artifact => artifact.RelativePath == "Dockerfile").Content;
        dockerfile.Should().Contain($"python:{expectedVersion}-slim-bookworm")
            .And.Contain($"ENV FUNCTIONS_WORKER_RUNTIME_VERSION={expectedVersion}")
            .And.Contain(PythonContainerArtifactProvider.WorkerPackageId)
            .And.Contain(PythonContainerArtifactProvider.WorkerPackageVersion)
            .And.Contain(PythonContainerArtifactProvider.WorkerPackageFeed)
            .And.Contain(PythonContainerArtifactProvider.WorkerPackageContentPath)
            .And.NotContain("{{");
        result.Artifacts.Single(artifact => artifact.RelativePath == "entrypoint.sh").Content.Should().NotContain("\r");
        if (requestedVersion is null)
        {
            ComputeHash(result).Should().Be("dc7a49ce35579fdd941fffcb3c80864d7b3cdeb514eb7bef02cf6733d7ad14a5");
        }
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedVersion_ThrowsProviderException()
    {
        var provider = new PythonContainerArtifactProvider();
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));
        ContainerArtifactRequest request = CreateRequest(project, "3.10");

        Func<Task> act = () => provider.GenerateAsync(request, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<ContainerArtifactProviderException>()
            .WithMessage("*version '3.10'*Python*3.11, 3.12, 3.13, 3.14*");
    }

    [Fact]
    public void Supports_ClaimsOnlyFlexByocPythonProjects()
    {
        var provider = new PythonContainerArtifactProvider();
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));

        provider.Supports(project, ContainerArtifactCapability.FlexByoc).Should().BeTrue();
        provider.Supports(project, ContainerArtifactCapability.Docker).Should().BeFalse();
    }

    private static ContainerArtifactRequest CreateRequest(PythonFunctionsProject project, string? runtimeVersion) => new(
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

    private static string GetProjectPath() => Path.Combine(Path.GetTempPath(), "func-container-provider-tests", "python");
}
