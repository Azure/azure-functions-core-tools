// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeDotNetBoundaryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-dotnet-boundary-").FullName;

    private string Hive => Path.Combine(_root, "hive");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("CSharp", ".csproj")]
    [InlineData("FSharp", ".fsproj")]
    public async Task UnchangedUpstreamProject_NativePreviewResolvesName_QualifiedFuncInvocationStillRefusesTopology(
        string language, string extension)
    {
        await InstallArchiveAsync();
        IProjectStack stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("dotnet");
        stack.DisplayName.Returns(".NET");
        stack.WorkerRuntimeAliases.Returns(["dotnet-isolated"]);
        stack.SupportedLanguages.Returns(["C#", "F#"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>
        {
            ["C#"] = ["csharp"],
            ["F#"] = ["fsharp"],
        });
        TemplateProjectSupport support = new(new(new([stack])), new(new PhysicalFileSystem()));
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        TemplateInvocationCoordinator invocation = new(session, new PhysicalFileSystem(), support);
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Microsoft.AzureFunctions.ProjectTemplate.{language}.Isolated.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string output = Path.Combine(_root, language);
        Dictionary<string, string?> parameters = new() { ["Framework"] = "net10.0" };
        TemplateCreator creator = new(session.Settings);
        ITemplateCreationResult preview = await creator.InstantiateAsync(template, "Sample", "Sample", output, parameters,
            dryRun: true, cancellationToken: CancellationToken.None);
        preview.Status.Should().Be(CreationResultStatus.Success);
        preview.CreationEffects!.CreationResult.PrimaryOutputs.Should().ContainSingle().Which.Path.Should().Be("Sample" + extension);

        TemplateInvocationResult refused = await invocation.InvokeAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(output), parameters, "Sample", DryRun: true), CancellationToken.None);

        refused.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        refused.Diagnostic.Should().Be("Project topology property 'sources' is not qualified for this creation path.");
        refused.Changes.Should().BeEmpty();
        refused.ConfiguredProjects.Should().BeEmpty();
        Directory.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData("CSharp")]
    [InlineData("FSharp")]
    public async Task NativeItem_WithoutExplicitActionCapability_RefusesBeforeMutation(string language)
    {
        await InstallArchiveAsync();
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        TemplateInvocationCoordinator invocation = new(session, new PhysicalFileSystem());
        ITemplateInfo template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == $"Azure.Function.{language}.Isolated.HttpTrigger.3.x");
        TemplateConfigurationSnapshot configuration = await session.ReadConfigurationAsync(template, CancellationToken.None);
        string output = Path.Combine(_root, language);

        TemplateInvocationResult refused = await invocation.InvokeAsync(template, configuration,
            new(WorkingDirectory.FromExplicit(output), new Dictionary<string, string?>(), "Greeting"), CancellationToken.None);

        refused.Status.Should().Be(TemplateInvocationStatus.Failed);
        refused.Diagnostic.Should().Contain("post-actions");
        refused.Changes.Should().BeEmpty();
        Directory.Exists(output).Should().BeFalse();
    }

    private async Task InstallArchiveAsync()
    {
        string outerPath = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string innerPath = Path.Combine(_root, "Azure.Functions.Templates.DotNet.nupkg");
        using (ZipArchive outer = ZipFile.OpenRead(outerPath))
        {
            outer.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(innerPath);
        }

        await TemplateFixtures.InstallAsync(innerPath, Hive);
    }
}
