// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Containers;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public class PowerShellContainerArtifactProviderTests
{
    [Theory]
    [InlineData(null, "7.6", "preview-7.6-ubuntu-24.04", "4.0.5362", PowerShellContainerArtifactProvider.InfrastructureFeed)]
    [InlineData("7.2", "7.2", "7.2-ubuntu-22.04", "4.0.4025", PowerShellContainerArtifactProvider.PowerShellWorkerFeed)]
    [InlineData("7.4", "7.4", "7.4-ubuntu-22.04", "4.0.5361", PowerShellContainerArtifactProvider.InfrastructureFeed)]
    [InlineData("7.6", "7.6", "preview-7.6-ubuntu-24.04", "4.0.5362", PowerShellContainerArtifactProvider.InfrastructureFeed)]
    public async Task GenerateAsync_RuntimeVersion_GeneratesExpectedArtifacts(
        string? requestedVersion,
        string expectedVersion,
        string expectedImageTag,
        string expectedWorkerVersion,
        string expectedFeed)
    {
        var provider = new PowerShellContainerArtifactProvider();
        var project = new PowerShellFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));
        ContainerArtifactRequest request = CreateRequest(project, requestedVersion);

        ContainerArtifactResult result = await provider.GenerateAsync(request, CancellationToken.None);

        result.Artifacts.Select(artifact => artifact.RelativePath).Should().Equal("Dockerfile", "entrypoint.sh", ".dockerignore");
        string dockerfile = result.Artifacts.Single(artifact => artifact.RelativePath == "Dockerfile").Content;
        dockerfile.Should().Contain($"mcr.microsoft.com/powershell:{expectedImageTag}")
            .And.Contain($"ENV FUNCTIONS_WORKER_RUNTIME_VERSION={expectedVersion}")
            .And.Contain($"Microsoft.Azure.Functions.PowerShellWorker.PS{expectedVersion}")
            .And.Contain(expectedWorkerVersion)
            .And.Contain(expectedFeed)
            .And.Contain($"microsoft.azure.functions.powershellworker.ps{expectedVersion}/{expectedWorkerVersion}/contentFiles/any/any/workers/powershell")
            .And.NotContain("{{");
        result.Artifacts.Single(artifact => artifact.RelativePath == "entrypoint.sh").Content.Should().NotContain("\r");
        if (requestedVersion is null)
        {
            ComputeHash(result).Should().Be("b09dd496e5c8ef1d59b103547e4d48fe5d015dca1af70f9bdac52050a8b856f7");
        }
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedVersion_ThrowsProviderException()
    {
        var provider = new PowerShellContainerArtifactProvider();
        var project = new PowerShellFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));
        ContainerArtifactRequest request = CreateRequest(project, "7.0");

        Func<Task> act = () => provider.GenerateAsync(request, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<ContainerArtifactProviderException>()
            .WithMessage("*version '7.0'*PowerShell*7.2, 7.4, 7.6*");
    }

    [Fact]
    public void Supports_ClaimsOnlyFlexByocPowerShellProjects()
    {
        var provider = new PowerShellContainerArtifactProvider();
        var project = new PowerShellFunctionsProject(WorkingDirectory.FromExplicit(GetProjectPath()));

        provider.Supports(project, ContainerArtifactCapability.FlexByoc).Should().BeTrue();
        provider.Supports(project, ContainerArtifactCapability.Docker).Should().BeFalse();
    }

    private static ContainerArtifactRequest CreateRequest(PowerShellFunctionsProject project, string? runtimeVersion) => new(
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

    private static string GetProjectPath() => Path.Combine(Path.GetTempPath(), "func-container-provider-tests", "powershell");
}
