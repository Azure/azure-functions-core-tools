// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Containers;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetContainerArtifactProviderTests
{
    [Theory]
    [InlineData(null, "11")]
    [InlineData("net8.0", "8")]
    [InlineData("9", "9")]
    [InlineData("10.0", "10")]
    [InlineData("net11.0", "11")]
    public async Task GenerateAsync_RuntimeVersion_GeneratesExpectedArtifacts(string? requestedVersion, string expectedVersion)
    {
        IDotNetProjectMetadataReader metadataReader = Substitute.For<IDotNetProjectMetadataReader>();
        metadataReader.ReadAssemblyName(Arg.Any<string>()).Returns("Custom.App");
        var provider = new DotNetContainerArtifactProvider(metadataReader);
        var root = WorkingDirectory.FromExplicit(GetProjectPath());
        var project = new DotNetSourceProject(root, Path.Combine(root.Info.FullName, "My App.csproj"), Substitute.For<IDotnetCliRunner>());
        ContainerArtifactRequest request = CreateRequest(project, requestedVersion);

        ContainerArtifactResult result = await provider.GenerateAsync(request, CancellationToken.None);

        result.Artifacts.Select(artifact => artifact.RelativePath).Should().Equal("Dockerfile", "entrypoint.sh", ".dockerignore");
        result.Artifacts.Single(artifact => artifact.RelativePath == "Dockerfile").Content.Should()
            .Contain($"mcr.microsoft.com/dotnet/sdk:{expectedVersion}.0-noble")
            .And.Contain($"mcr.microsoft.com/dotnet/aspnet:{expectedVersion}.0-noble")
            .And.Contain("dotnet publish './My App.csproj'")
            .And.NotContain("{{");
        result.Artifacts.Single(artifact => artifact.RelativePath == "entrypoint.sh").Content.Should()
            .Contain("exec dotnet '/home/site/wwwroot/Custom.App.dll'")
            .And.NotContain("\r");
        result.Artifacts.Single(artifact => artifact.RelativePath == ".dockerignore").Content.Should().Be(
            ".git\n.gitignore\n.vscode\n.azure\nbin\nobj\nlocal.settings.json\n*.user\n.env\n.env.*\n");
        if (requestedVersion is null)
        {
            ComputeHash(result).Should().Be("328dd462d1af523c945843a68295f91bb7bf9f7be8406ff576e4d3e3a1167a94");
        }
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedVersion_ThrowsProviderException()
    {
        var provider = new DotNetContainerArtifactProvider(Substitute.For<IDotNetProjectMetadataReader>());
        var root = WorkingDirectory.FromExplicit(GetProjectPath());
        var project = new DotNetSourceProject(root, Path.Combine(root.Info.FullName, "sample.csproj"), Substitute.For<IDotnetCliRunner>());
        ContainerArtifactRequest request = CreateRequest(project, "net7.0");

        Func<Task> act = () => provider.GenerateAsync(request, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<ContainerArtifactProviderException>()
            .WithMessage("*version 'net7.0'*.NET isolated*8, 9, 10, 11*");
    }

    [Fact]
    public void Supports_ClaimsOnlyFlexByocDotNetSourceProjects()
    {
        var provider = new DotNetContainerArtifactProvider(Substitute.For<IDotNetProjectMetadataReader>());
        var root = WorkingDirectory.FromExplicit(GetProjectPath());
        var project = new DotNetSourceProject(root, Path.Combine(root.Info.FullName, "sample.csproj"), Substitute.For<IDotnetCliRunner>());

        provider.Supports(project, ContainerArtifactCapability.FlexByoc).Should().BeTrue();
        provider.Supports(project, ContainerArtifactCapability.Docker).Should().BeFalse();
    }

    private static ContainerArtifactRequest CreateRequest(DotNetSourceProject project, string? runtimeVersion) => new(
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

    private static string GetProjectPath() => Path.Combine(Path.GetTempPath(), "func-container-provider-tests", "dotnet");
}
