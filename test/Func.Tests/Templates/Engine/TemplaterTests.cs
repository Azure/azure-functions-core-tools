// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterTests : IDisposable
{
    private readonly string _root;
    private readonly string _hive;

    public TemplaterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-templater-tests-" + Guid.NewGuid().ToString("N"));
        _hive = Path.Combine(_root, "hive");
        Directory.CreateDirectory(_hive);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }

    [Fact]
    public async Task ListAsync_Item_ReturnsItemTemplateEntries()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new TemplateCatalogEntry(
            Identity: TemplateFixtures.ItemTemplateIdentity,
            Name: "Func Tests Item",
            Description: null,
            Type: TemplateType.Item,
            ShortNames: [TemplateFixtures.ItemTemplateShortName],
            GroupIdentity: null,
            Language: null,
            Precedence: 0,
            Package: new TemplatePackageInfo(packageDirectory, Version: null),
            IsHidden: false,
            Parameters: [],
            MetadataDiagnostic: null));
    }

    [Fact]
    public async Task ListAsync_Project_ReturnsProjectTemplateEntries()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Project, CancellationToken.None);

        TemplateCatalogEntry entry = entries.Should().ContainSingle().Subject;
        entry.Identity.Should().Be(TemplateFixtures.ProjectTemplateIdentity);
        entry.Type.Should().Be(TemplateType.Project);
    }

    [Fact]
    public async Task ListAsync_TemplatesWithoutItemOrProjectType_AreNotListed()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);

        // The engine installs both, so only their type keeps them out of the catalog.
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.OtherTypeTemplateIdentity);
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.UntypedTemplateIdentity);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> items = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        IReadOnlyList<TemplateCatalogEntry> projects = await templater.ListAsync(TemplateType.Project, CancellationToken.None);

        items.Concat(projects).Select(entry => entry.Identity).Should().BeEquivalentTo(
            [TemplateFixtures.ItemTemplateIdentity, TemplateFixtures.ProjectTemplateIdentity]);
    }

    [Fact]
    public async Task ListAsync_FuncHostMetadata_AppliesParameterAliases()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        TemplateCatalogEntry entry = Entry(entries, TemplateFixtures.MetadataTemplateIdentity);
        entry.Language.Should().Be("TypeScript");
        entry.MetadataDiagnostic.Should().BeNull();
        entry.Parameters.Select(parameter => (parameter.CanonicalName, parameter.LongName, parameter.ShortName)).Should().Equal(
            ("Level", "log-level", "l"),
            ("Namespace", "Namespace", null));
    }

    [Fact]
    public async Task ListAsync_HiddenTemplate_IsListedAsHidden()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        Entry(entries, TemplateFixtures.HiddenTemplateIdentity).IsHidden.Should().BeTrue();
        Entry(entries, TemplateFixtures.DotnetHostOnlyTemplateIdentity).IsHidden
            .Should().BeFalse("dotnetcli.host.json only applies to dotnet new");
    }

    [Fact]
    public async Task ListAsync_InvalidFuncHostMetadata_ReportsDiagnosticOnEntry()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        TemplateCatalogEntry entry = Entry(entries, TemplateFixtures.InvalidHostTemplateIdentity);
        entry.MetadataDiagnostic.Should().Contain("func.host.json");
        entry.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_FromEngineCache_ReturnsSameEntriesAsFromScan()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), _hive);
        IReadOnlyList<TemplateCatalogEntry> scanned = await ListWithNewTemplaterAsync(TemplateType.Item);

        IReadOnlyList<TemplateCatalogEntry> cached = await ListWithNewTemplaterAsync(TemplateType.Item);

        scanned.Select(entry => entry.Identity).Should().BeEquivalentTo(
        [
            TemplateFixtures.MetadataTemplateIdentity,
            TemplateFixtures.InvalidHostTemplateIdentity,
            TemplateFixtures.DotnetHostOnlyTemplateIdentity,
            TemplateFixtures.HiddenTemplateIdentity,
        ]);
        cached.Should().BeEquivalentTo(scanned);
    }

    [Fact]
    public async Task ListAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using Templater templater = CreateTemplater();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => templater.ListAsync(TemplateType.Item, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveArtifactTemplatesAsync_MatchingSelectors_ProjectsArguments()
    {
        using Templater templater = CreateArtifactTemplater();

        IReadOnlyList<ResolvedTemplate> templates = await templater.ResolveArtifactTemplatesAsync(
            "container",
            "dotnet",
            "C#",
            CancellationToken.None);

        ResolvedTemplate template = templates.Should().ContainSingle().Subject;
        template.Entry.Identity.Should().Be(TemplateFixtures.ArtifactTemplateIdentity);
        template.Entry.Type.Should().Be(TemplateType.Artifact);
        template.Entry.Stack.Should().Be("dotnet");
        template.Entry.Capability.Should().Be("container");
        template.Parameters.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TemplateParameterDefinition(
                CanonicalName: "Runtime",
                LongName: "runtime",
                ShortName: "r",
                Description: null,
                DataType: "choice",
                Choices:
                [
                    new TemplateParameterChoice("8.0", null, null),
                    new TemplateParameterChoice("9.0", null, null),
                ],
                DefaultValue: null,
                IsRequired: true,
                IsHidden: false,
                AlwaysShow: false));
    }

    [Fact]
    public async Task ResolveArtifactTemplatesAsync_NonMatchingSelectors_ReturnsEmpty()
    {
        using Templater templater = CreateArtifactTemplater();

        IReadOnlyList<ResolvedTemplate> templates = await templater.ResolveArtifactTemplatesAsync(
            "container",
            "python",
            "Python",
            CancellationToken.None);

        templates.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_DryRun_ReturnsChangesWithoutWritingFiles()
    {
        using Templater templater = CreateArtifactTemplater();
        ResolvedTemplate template = (await templater.ResolveArtifactTemplatesAsync(
            "container",
            "dotnet",
            "C#",
            CancellationToken.None)).Should().ContainSingle().Subject;
        string outputPath = Path.Combine(_root, "dry-run-output");

        TemplateInvocationResult result = await template.InvokeAsync(
            new(outputPath, new Dictionary<string, string> { ["Runtime"] = "8.0" }, Force: false, TemplateInvocationMode.DryRun),
            CancellationToken.None);

        result.WasCreated.Should().BeFalse();
        result.FileChanges.Should().ContainSingle().Which.Should().Be(new TemplateFileChange("artifact.txt", TemplateFileChangeKind.Create));
        File.Exists(Path.Combine(outputPath, "artifact.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Create_WritesProjectedArtifact()
    {
        using Templater templater = CreateArtifactTemplater();
        ResolvedTemplate template = (await templater.ResolveArtifactTemplatesAsync(
            "container",
            "dotnet",
            "C#",
            CancellationToken.None)).Should().ContainSingle().Subject;
        string outputPath = Path.Combine(_root, "create-output");

        TemplateInvocationResult result = await template.InvokeAsync(
            new(outputPath, new Dictionary<string, string> { ["Runtime"] = "9.0" }, Force: false, TemplateInvocationMode.Create),
            CancellationToken.None);

        result.WasCreated.Should().BeTrue();
        File.ReadAllText(Path.Combine(outputPath, "artifact.txt")).Should().Be("runtime=9.0");
    }

    [Fact]
    public async Task TemplateArgumentParser_ValidAliases_SelectsTemplateAndProjectsValues()
    {
        using Templater templater = CreateArtifactTemplater();
        IReadOnlyList<ResolvedTemplate> templates = await templater.ResolveArtifactTemplatesAsync(
            "container",
            "dotnet",
            "C#",
            CancellationToken.None);
        TemplateArgumentParser parser = new();

        TemplateArgumentParseResult result = parser.Parse(templates, ["-r", "8.0"]);

        result.IsSuccess.Should().BeTrue();
        result.Template!.Entry.Identity.Should().Be(TemplateFixtures.ArtifactTemplateIdentity);
        result.Values.Should().Contain("Runtime", "8.0");
    }

    [Fact]
    public async Task TemplateArgumentParser_InvalidChoice_ReturnsSupportedValues()
    {
        using Templater templater = CreateArtifactTemplater();
        IReadOnlyList<ResolvedTemplate> templates = await templater.ResolveArtifactTemplatesAsync(
            "container",
            "dotnet",
            "C#",
            CancellationToken.None);
        TemplateArgumentParser parser = new();

        TemplateArgumentParseResult result = parser.Parse(templates, ["--runtime", "10.0"]);

        result.IsSuccess.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().ContainAll("10.0", "8.0", "9.0");
    }

    [Fact]
    public async Task ListAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.ListAsync(TemplateType.Item, CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_NullSession_Throws()
    {
        Action act = () => _ = new Templater(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    private async Task<IReadOnlyList<TemplateCatalogEntry>> ListMetadataTemplatesAsync()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), _hive);
        return await ListWithNewTemplaterAsync(TemplateType.Item);
    }

    private async Task<IReadOnlyList<TemplateCatalogEntry>> ListWithNewTemplaterAsync(TemplateType type)
    {
        using Templater templater = CreateTemplater();
        return await templater.ListAsync(type, CancellationToken.None);
    }

    private static TemplateCatalogEntry Entry(IReadOnlyList<TemplateCatalogEntry> entries, string identity)
        => entries.Should().ContainSingle(candidate => candidate.Identity == identity).Subject;

    private Templater CreateTemplater()
        => new(new TemplateEngineSession(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive));

    private Templater CreateArtifactTemplater()
    {
        string packageDirectory = TemplateFixtures.WriteArtifactPackage(_root);
        return new(new TemplateEngineSession(
            new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            _hive,
            [new WorkloadTemplatePackageSource("Test.Workload", packageDirectory)]));
    }
}
