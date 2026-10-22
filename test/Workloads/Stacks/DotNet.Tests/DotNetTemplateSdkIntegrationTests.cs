// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class DotNetTemplateSdkIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-template-sdk-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(".csproj")]
    [InlineData(".fsproj")]
    public async Task Preparation_RealSdk_EvaluatesImportedProjectPropertiesWithoutRestore(string extension)
    {
        string project = Path.Combine(_root, "Actual Project" + extension);
        File.WriteAllText(Path.Combine(_root, "settings.props"),
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Imported.Functions</RootNamespace></PropertyGroup></Project>");
        File.WriteAllText(project, """<Project Sdk="Microsoft.NET.Sdk"><Import Project="settings.props" /></Project>""");
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotNetTemplateProjectPreparation preparation = new(new DotnetCliRunner(resolver));

        DotNetTemplateProjectContext result = await preparation.ReadAsync(project, CancellationToken.None);

        result.Should().Be(new DotNetTemplateProjectContext(project, "net10.0", "Imported.Functions"));
        File.Exists(Path.Combine(_root, "obj", "project.assets.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "bin")).Should().BeFalse();
    }
}
