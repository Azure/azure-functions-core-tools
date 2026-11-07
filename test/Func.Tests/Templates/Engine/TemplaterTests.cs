// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

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
    public async Task ListAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.ListAsync(TemplateType.Item, CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ResolveAsync_FullIdentity_IgnoresCaseAndDoesNotWidenToSiblingVariants()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("javascript", "Func.Tests.Http.JavaScript", "http", "Func.Tests.Http", "JavaScript", null, null),
            ("python", "Func.Tests.Http.Python", "http", "Func.Tests.Http", "Python", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult exact = await templater.ResolveAsync("func.tests.http.javascript", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult alias = await templater.ResolveAsync("http", TemplateType.Item, CancellationToken.None);

        exact.Status.Should().Be(TemplateResolutionStatus.Selected);
        exact.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.Http.JavaScript");
        exact.Candidates.Should().ContainSingle();
        alias.Status.Should().Be(TemplateResolutionStatus.Ambiguous);
        alias.SelectedTemplate.Should().BeNull();
        alias.Candidates.Select(candidate => candidate.Identity)
            .Should().BeEquivalentTo(["Func.Tests.Http.JavaScript", "Func.Tests.Http.Python"]);

        TemplateCatalogEntry chosen = alias.Candidates.Single(candidate => candidate.Identity == "Func.Tests.Http.JavaScript");
        TemplateResolutionResult chosenResult = await templater.ResolveAsync(chosen, TemplateType.Item, CancellationToken.None);
        chosenResult.Status.Should().Be(TemplateResolutionStatus.Selected);
        chosenResult.SelectedTemplate!.Descriptor.Identity.Should().Be(chosen.Identity);
    }

    [Fact]
    public async Task ResolveAsync_FullIdentityMatchingAnotherAlias_PrefersTheIdentity()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("identity", "Func.Tests.Shared", "identity-alias", null, null, null, null),
            ("alias", "Func.Tests.Other", "Func.Tests.Shared", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("func.tests.shared", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Selected);
        result.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.Shared");
    }

    [Fact]
    public async Task ResolveAsync_AliasMatchingOneVariant_DoesNotIncludeSiblingsWithDifferentAliases()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("first", "Func.Tests.First", "first", "Func.Tests.Family", null, null, null),
            ("second", "Func.Tests.Second", "second", "Func.Tests.Family", null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("first", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Selected);
        result.Candidates.Should().ContainSingle().Which.Identity.Should().Be("Func.Tests.First");
    }

    [Fact]
    public async Task ResolveAsync_AliasMatchingDifferentFamilies_ReportsAmbiguity()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("first", "Func.Tests.First", "shared", "Func.Tests.FamilyA", null, null, null),
            ("second", "Func.Tests.Second", "shared", "Func.Tests.FamilyB", null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("shared", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Ambiguous);
        result.SelectedTemplate.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "ambiguous" && diagnostic.Message.Contains("multiple eligible templates"));
        result.Candidates.Select(candidate => candidate.Identity).Should().BeEquivalentTo(["Func.Tests.First", "Func.Tests.Second"]);
    }

    [Fact]
    public async Task ResolveAsync_CachedAliasBecomesAmbiguous_UsesCurrentAliasesAndPreservesExactSelections()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("first", "Func.Tests.First", "first", null, null, null, null),
            ("second", "Func.Tests.Second", "second", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        TemplateCatalogEntry secondEntry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None))
            .Single(entry => entry.Identity == "Func.Tests.Second");
        TemplateResolutionResult initial = await templater.ResolveAsync("first", TemplateType.Item, CancellationToken.None);

        string configurationPath = Path.Combine(packageDirectory, "second", ".template.config", "template.json");
        System.Text.Json.Nodes.JsonObject configuration = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(configurationPath))!.AsObject();
        configuration["shortName"] = "first";
        await File.WriteAllTextAsync(configurationPath, configuration.ToJsonString());

        TemplateResolutionResult alias = await templater.ResolveAsync("first", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult retiredAlias = await templater.ResolveAsync("second", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult firstIdentity = await templater.ResolveAsync("Func.Tests.First", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult secondIdentity = await templater.ResolveAsync("Func.Tests.Second", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult selectedEntry = await templater.ResolveAsync(secondEntry, TemplateType.Item, CancellationToken.None);
        IReadOnlyList<TemplateCatalogEntry> currentEntries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        initial.Status.Should().Be(TemplateResolutionStatus.Selected);
        initial.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.First");
        alias.Status.Should().Be(TemplateResolutionStatus.Ambiguous);
        alias.SelectedTemplate.Should().BeNull();
        alias.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "ambiguous");
        alias.Candidates.Select(entry => entry.Identity).Should().BeEquivalentTo(["Func.Tests.First", "Func.Tests.Second"]);
        alias.Candidates.Should().OnlyContain(entry => entry.ShortNames.Contains("first"));
        retiredAlias.Status.Should().Be(TemplateResolutionStatus.NotFound);
        retiredAlias.SelectedTemplate.Should().BeNull();
        firstIdentity.Status.Should().Be(TemplateResolutionStatus.Selected);
        firstIdentity.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.First");
        secondIdentity.Status.Should().Be(TemplateResolutionStatus.Selected);
        secondIdentity.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.Second");
        secondIdentity.SelectedTemplate.Descriptor.ShortNames.Should().Equal("first");
        selectedEntry.Status.Should().Be(TemplateResolutionStatus.Selected);
        selectedEntry.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.Second");
        selectedEntry.SelectedTemplate.Descriptor.ShortNames.Should().Equal("first");
        currentEntries.Single(entry => entry.Identity == "Func.Tests.Second").ShortNames.Should().Equal("first");
    }

    [Theory]
    [InlineData("type", "project")]
    [InlineData("language", "Python")]
    public async Task ResolveAsync_ChangedEligibilityTag_FailsInsteadOfUsingCachedMetadata(string tag, string value)
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("item", "Func.Tests.Item", "item", null, "JavaScript", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater(context: ProjectContext("node", "JavaScript"), projectStacks: ProjectStacks());
        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();
        (await templater.ResolveAsync("item", TemplateType.Item, CancellationToken.None)).Status.Should().Be(TemplateResolutionStatus.Selected);

        string configurationPath = Path.Combine(packageDirectory, "item", ".template.config", "template.json");
        System.Text.Json.Nodes.JsonObject configuration = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(configurationPath))!.AsObject();
        configuration["tags"]!.AsObject()[tag] = value;
        await File.WriteAllTextAsync(configurationPath, configuration.ToJsonString());

        TemplateResolutionResult alias = await templater.ResolveAsync("item", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult identity = await templater.ResolveAsync("Func.Tests.Item", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult selectedEntry = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult missing = await templater.ResolveAsync("not-installed", TemplateType.Item, CancellationToken.None);
        Func<Task> list = () => templater.ListAsync(TemplateType.Item, CancellationToken.None);

        foreach (TemplateResolutionResult result in new[] { alias, identity, selectedEntry, missing })
        {
            result.Status.Should().Be(TemplateResolutionStatus.Failed);
            result.SelectedTemplate.Should().BeNull();
            result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("installed-template-inventory-stale");
        }

        await list.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Func.Tests.Item*");
    }

    [Fact]
    public async Task ResolveAsync_ChangedCompetingLanguage_FailsAliasButKeepsUnaffectedExactIdentity()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("first", "Func.Tests.First", "shared", null, "JavaScript", null, null),
            ("second", "Func.Tests.Second", "shared", null, "Python", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater(context: ProjectContext("node", "JavaScript"), projectStacks: ProjectStacks());
        TemplateResolutionResult initial = await templater.ResolveAsync("shared", TemplateType.Item, CancellationToken.None);

        string configurationPath = Path.Combine(packageDirectory, "second", ".template.config", "template.json");
        System.Text.Json.Nodes.JsonObject configuration = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(configurationPath))!.AsObject();
        configuration["tags"]!.AsObject()["language"] = "JavaScript";
        await File.WriteAllTextAsync(configurationPath, configuration.ToJsonString());

        TemplateResolutionResult alias = await templater.ResolveAsync("shared", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult firstIdentity = await templater.ResolveAsync("Func.Tests.First", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult secondIdentity = await templater.ResolveAsync("Func.Tests.Second", TemplateType.Item, CancellationToken.None);

        initial.Status.Should().Be(TemplateResolutionStatus.Selected);
        initial.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.First");
        alias.Status.Should().Be(TemplateResolutionStatus.Failed);
        alias.SelectedTemplate.Should().BeNull();
        alias.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("installed-template-inventory-stale");
        firstIdentity.Status.Should().Be(TemplateResolutionStatus.Selected);
        firstIdentity.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.First");
        secondIdentity.Status.Should().Be(TemplateResolutionStatus.Failed);
        secondIdentity.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("installed-template-inventory-stale");
    }

    [Theory]
    [InlineData(null, "{\"isHidden\":true}")]
    [InlineData("{\"isHidden\":false}", "{\"isHidden\":true}")]
    [InlineData("{\"isHidden\":true}", "{\"isHidden\":false}")]
    [InlineData("{\"isHidden\":false}", "{ not valid json")]
    public async Task ResolveAsync_ChangedFuncHostMetadata_FailsInsteadOfUsingCachedEligibility(string? initialHostData, string currentHostData)
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("item", "Func.Tests.Item", "item", null, null, null, null));
        string hostPath = Path.Combine(packageDirectory, "item", ".template.config", "func.host.json");
        if (initialHostData is not null)
        {
            await File.WriteAllTextAsync(hostPath, initialHostData);
        }

        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();

        await File.WriteAllTextAsync(hostPath, currentHostData);
        TemplateResolutionResult alias = await templater.ResolveAsync("item", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult identity = await templater.ResolveAsync("Func.Tests.Item", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult selectedEntry = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);

        foreach (TemplateResolutionResult result in new[] { alias, identity, selectedEntry })
        {
            result.Status.Should().Be(TemplateResolutionStatus.Failed);
            result.SelectedTemplate.Should().BeNull();
            TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
            diagnostic.Code.Should().Be("installed-template-inventory-stale");
            diagnostic.Message.Should().Contain("func.host.json");
        }
    }

    [Fact]
    public async Task ResolveAsync_UnknownReferenceAndWrongKind_ReportDistinctFailures()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult missing = await templater.ResolveAsync("not-installed", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult wrongKind = await templater.ResolveAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Item, CancellationToken.None);

        missing.Status.Should().Be(TemplateResolutionStatus.NotFound);
        wrongKind.Status.Should().Be(TemplateResolutionStatus.Failed);
        missing.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("not-found");
        wrongKind.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("wrong-kind");
        missing.SelectedTemplate.Should().BeNull();
        wrongKind.SelectedTemplate.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_RegistrationsSharingOneMount_DoNotCreateDuplicateIdentities()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        TemplateEngineSession session = new(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive);
#pragma warning disable CS0618 // The engine exposes test provider registration through this API.
        session.Settings.Components.Register(typeof(SharedMountProviderFactory));
#pragma warning restore CS0618
        using Templater templater = new(session, new InstalledProjectStackCatalog([]));
        IReadOnlyList<ITemplatePackage> packages = await session.PackageManager.GetTemplatePackagesAsync(force: false, CancellationToken.None);
        packages.Count(package => package.MountPointUri == packageDirectory).Should().Be(2);

        TemplateResolutionResult byIdentity = await templater.ResolveAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);
        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();
        TemplateResolutionResult byEntry = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);

        byIdentity.Status.Should().Be(TemplateResolutionStatus.Selected);
        byEntry.Status.Should().Be(TemplateResolutionStatus.Selected);
        byIdentity.SelectedTemplate!.Descriptor.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
        byEntry.SelectedTemplate!.Descriptor.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task ResolveAsync_DuplicateIdentitiesAcrossPackages_FailsRegardlessOfCase()
    {
        string first = TemplateFixtures.WriteSelectionPackage(
            Path.Combine(_root, "first"),
            ("one", "Func.Tests.Duplicate", "first", null, null, null, null));
        string second = TemplateFixtures.WriteSelectionPackage(
            Path.Combine(_root, "second"),
            ("two", "func.tests.duplicate", "second", null, null, null, null));
        await TemplateFixtures.InstallAsync(first, _hive);
        await TemplateFixtures.InstallAsync(second, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("FUNC.TESTS.DUPLICATE", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.SelectedTemplate.Should().BeNull();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("first").And.Contain("second");
    }

    [Fact]
    public async Task ResolveAsync_AliasOfDuplicateIdentity_FailsInsteadOfChoosingOnePackage()
    {
        string first = TemplateFixtures.WriteSelectionPackage(
            Path.Combine(_root, "first"),
            ("one", "Func.Tests.Duplicate", "shared", null, null, null, null));
        string second = TemplateFixtures.WriteSelectionPackage(
            Path.Combine(_root, "second"),
            ("two", "Func.Tests.Duplicate", "shared", null, null, null, null));
        await TemplateFixtures.InstallAsync(first, _hive);
        await TemplateFixtures.InstallAsync(second, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("shared", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.SelectedTemplate.Should().BeNull();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("duplicate-identity");
    }

    [Fact]
    public async Task ResolveAsync_DuplicateIdentityWithinPackage_Fails()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("one", "Func.Tests.Duplicate", "first", null, null, null, null),
            ("two", "Func.Tests.Duplicate", "second", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("Func.Tests.Duplicate", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("duplicate-identity");
        result.SelectedTemplate.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_IneligibleExactIdentity_DoesNotSubstituteMatchingAlias()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("python", "Func.Tests.Python", "python", null, "Python", null, null),
            ("javascript", "Func.Tests.JavaScript", "Func.Tests.Python", null, "JavaScript", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater(context: ProjectContext("node", "JavaScript"), projectStacks: ProjectStacks());

        TemplateResolutionResult result = await templater.ResolveAsync("Func.Tests.Python", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.SelectedTemplate.Should().BeNull();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("language-mismatch");
    }

    [Fact]
    public async Task ResolveAsync_AliasFiltersLanguages_RetainsTheRejectedCandidateDiagnostic()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("javascript", "Func.Tests.JavaScript", "shared", null, "JavaScript", null, null),
            ("python", "Func.Tests.Python", "shared", null, "Python", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater(context: ProjectContext("node", "JavaScript"), projectStacks: ProjectStacks());

        TemplateResolutionResult result = await templater.ResolveAsync("shared", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Selected);
        result.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.JavaScript");
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("language-mismatch");
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("js")]
    public async Task ResolveAsync_ProjectLanguageOrAlias_MatchesTheCanonicalTemplateLanguage(string language)
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("javascript", "Func.Tests.JavaScript", "javascript", null, "JavaScript", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater(context: ProjectContext("node", language), projectStacks: ProjectStacks());

        TemplateResolutionResult result = await templater.ResolveAsync("javascript", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Selected);
        result.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.JavaScript");
    }

    [Fact]
    public async Task ResolveAsync_SingleLanguageStackWithoutStoredLanguage_UsesItsOnlyLanguage()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("python", "Func.Tests.Python", "python", null, "Python", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater(context: ProjectContext("python", null), projectStacks: ProjectStacks());

        TemplateResolutionResult result = await templater.ResolveAsync("python", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Selected);
        result.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.Python");
    }

    [Theory]
    [InlineData(null, "JavaScript", "missing-project-stack")]
    [InlineData("node", null, "missing-project-language")]
    [InlineData("unknown", "JavaScript", "unsupported-project-stack")]
    [InlineData("node", "py", "inconsistent-project-context")]
    public async Task ResolveAsync_IncompleteOrInconsistentProjectContext_FailsClosed(
        string? stack,
        string? language,
        string expectedCode)
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("javascript", "Func.Tests.JavaScript", "javascript", null, "JavaScript", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater(context: ProjectContext(stack, language), projectStacks: ProjectStacks());

        TemplateResolutionResult result = await templater.ResolveAsync("javascript", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.SelectedTemplate.Should().BeNull();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task ResolveAsync_HiddenOrInvalidHostMetadata_FailsClosed()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult hidden = await templater.ResolveAsync(TemplateFixtures.HiddenTemplateIdentity, TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult invalid = await templater.ResolveAsync(TemplateFixtures.InvalidHostTemplateIdentity, TemplateType.Item, CancellationToken.None);

        hidden.Status.Should().Be(TemplateResolutionStatus.Failed);
        hidden.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("hidden");
        invalid.Status.Should().Be(TemplateResolutionStatus.Failed);
        invalid.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-metadata");
    }

    [Fact]
    public async Task ResolveAsync_UnknownConstraintAndAbsentWorkloadInventory_FailClosed()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("unknown", "Func.Tests.Unknown", "unknown", null, null, "future-constraint", null),
            ("workload", "Func.Tests.Workload", "workload", null, null, "func-workload", "node"));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult unknown = await templater.ResolveAsync("unknown", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult workload = await templater.ResolveAsync("workload", TemplateType.Item, CancellationToken.None);

        unknown.Status.Should().Be(TemplateResolutionStatus.Failed);
        unknown.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-not-evaluated");
        workload.Status.Should().Be(TemplateResolutionStatus.Failed);
        workload.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-not-evaluated");
    }

    [Fact]
    public async Task ResolveAsync_BundleConstraint_UsesTheResolvedBundleContext()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("bundle", "Func.Tests.Bundle", "bundle", null, null, "func-bundle", new { id = "test.bundle", version = "[4.20.0,5.0.0)" }));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater matching = CreateTemplater(context: new(
            WorkingDirectory.FromExplicit(_root),
            bundle: new TemplateEngineBundleContext("test.bundle", "4.25.0")));
        using Templater restricted = CreateTemplater(context: new(
            WorkingDirectory.FromExplicit(_root),
            bundle: new TemplateEngineBundleContext("other.bundle", "4.25.0")));
        using Templater missing = CreateTemplater();

        TemplateResolutionResult allowed = await matching.ResolveAsync("bundle", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult wrongBundle = await restricted.ResolveAsync("bundle", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult noBundle = await missing.ResolveAsync("bundle", TemplateType.Item, CancellationToken.None);

        allowed.Status.Should().Be(TemplateResolutionStatus.Selected);
        wrongBundle.Status.Should().Be(TemplateResolutionStatus.Failed);
        wrongBundle.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-restricted");
        noBundle.Status.Should().Be(TemplateResolutionStatus.Failed);
        noBundle.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-restricted");
    }

    [Fact]
    public async Task ResolveAsync_ChangedMountedHostConstraint_UsesTheMountedArguments()
    {
        using var args = System.Text.Json.JsonDocument.Parse("""[{"hostname":"func"}]""");
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("host", "Func.Tests.Host", "host", null, null, "host", args.RootElement.Clone()));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        _ = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        string configPath = Path.Combine(packageDirectory, "host", ".template.config", "template.json");
        string configuration = await File.ReadAllTextAsync(configPath);

        TemplateResolutionResult eligible = await templater.ResolveAsync("host", TemplateType.Item, CancellationToken.None);
        await File.WriteAllTextAsync(configPath, configuration.Replace("\"hostname\":\"func\"", "\"hostname\":\"other\"", StringComparison.Ordinal));
        TemplateResolutionResult restricted = await templater.ResolveAsync("host", TemplateType.Item, CancellationToken.None);

        eligible.Status.Should().Be(TemplateResolutionStatus.Selected);
        restricted.Status.Should().Be(TemplateResolutionStatus.Failed);
        restricted.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-restricted");
    }

    [Fact]
    public async Task ResolveAsync_MalformedMountedConstraint_ReportsInvalidMetadata()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("workload", "Func.Tests.Workload", "workload", null, null, "func-workload", "node"));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        _ = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        string configPath = Path.Combine(packageDirectory, "workload", ".template.config", "template.json");
        System.Text.Json.Nodes.JsonObject configuration = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        configuration["constraints"]!["func-workload"]!["args"] = new System.Text.Json.Nodes.JsonArray();
        await File.WriteAllTextAsync(configPath, configuration.ToJsonString());

        TemplateResolutionResult result = await templater.ResolveAsync("workload", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-constraints");
    }

    [Fact]
    public async Task ResolveAsync_AdditionalConfiguration_FailsClosedWithoutEvaluatingPartialConstraints()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("merged", "Func.Tests.Merged", "merged", null, null, null, null));
        string configDirectory = Path.Combine(packageDirectory, "merged", ".template.config");
        string configPath = Path.Combine(configDirectory, "template.json");
        System.Text.Json.Nodes.JsonObject configuration = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(configPath))!.AsObject();
        configuration["AdditionalConfigFiles"] = new System.Text.Json.Nodes.JsonArray("overrides.template.json");
        await File.WriteAllTextAsync(configPath, configuration.ToJsonString());
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "overrides.template.json"),
            """{"constraints":{"host":{"type":"host","args":[{"hostname":"other"}]}}}""");
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("merged", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("additional-configuration-not-evaluated");
    }

    [Fact]
    public async Task ResolveAsync_CatalogSelection_IsBoundToTheIssuingLiveSession()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater owner = CreateTemplater();
        using Templater foreign = CreateTemplater();
        TemplateCatalogEntry entry = (await owner.ListAsync(TemplateType.Item, CancellationToken.None)).Single();

        TemplateResolutionResult resolved = await owner.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult copied = await owner.ResolveAsync(entry with { }, TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult foreignEntry = await foreign.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult foreignResolvedEntry = await foreign.ResolveAsync(resolved.SelectedTemplate!.Descriptor, TemplateType.Item, CancellationToken.None);

        resolved.Status.Should().Be(TemplateResolutionStatus.Selected);
        ResolvedTemplate selection = resolved.SelectedTemplate!;
        copied.Status.Should().Be(TemplateResolutionStatus.Failed);
        copied.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-selection");
        foreignEntry.Status.Should().Be(TemplateResolutionStatus.Failed);
        foreignEntry.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-selection");
        foreignResolvedEntry.Status.Should().Be(TemplateResolutionStatus.Failed);
        foreignResolvedEntry.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-selection");

        owner.Dispose();
        selection.Descriptor.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
        Func<Task> resolveDisposed = () => owner.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);
        Func<Task> resolveSelectionDisposed = () => owner.ResolveAsync(selection.Descriptor, TemplateType.Item, CancellationToken.None);
        await resolveDisposed.Should().ThrowAsync<ObjectDisposedException>();
        await resolveSelectionDisposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ResolveAsync_CatalogSelectionWhoseConfigurationWasRemoved_FailsClosed()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();
        File.Delete(Path.Combine(packageDirectory, "item", ".template.config", "template.json"));

        TemplateResolutionResult result = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);

        result.SelectedTemplate.Should().BeNull();
        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == "constraint-source-unavailable"
            || diagnostic.Code == "stale-selection"
            || diagnostic.Code == "installed-template-unavailable");
    }

    [Fact]
    public async Task ResolveAsync_InstalledPackageRemovedAfterListing_ReportsUnavailableInsteadOfNotFound()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();
        Directory.Move(packageDirectory, Path.Combine(_root, "removed-package"));

        TemplateResolutionResult unknown = await templater.ResolveAsync("not-installed", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult selected = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);
        Func<Task> list = () => templater.ListAsync(TemplateType.Item, CancellationToken.None);

        unknown.Status.Should().Be(TemplateResolutionStatus.Failed);
        unknown.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("installed-package-unavailable");
        selected.Status.Should().Be(TemplateResolutionStatus.Failed);
        selected.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("installed-package-unavailable");
        await list.Should().ThrowAsync<InvalidOperationException>().WithMessage("*" + packageDirectory + "*");
    }

    [Fact]
    public async Task ResolveAsync_InstalledPackageMissingBeforeDiscovery_ReportsUnavailable()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        Directory.Move(packageDirectory, Path.Combine(_root, "removed-package"));
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("not-installed", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("installed-package-unavailable");
    }

    [Fact]
    public async Task ResolveAsync_CorruptInstalledConfiguration_ReportsIncompleteInventory()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        string configurationPath = Path.Combine(packageDirectory, "item", ".template.config", "template.json");
        await File.WriteAllTextAsync(configurationPath, "{ not valid json");
        using Templater templater = CreateTemplater();

        TemplateResolutionResult unknown = await templater.ResolveAsync("not-installed", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult healthy = await templater.ResolveAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);
        Func<Task> list = () => templater.ListAsync(TemplateType.Item, CancellationToken.None);

        unknown.Status.Should().Be(TemplateResolutionStatus.Failed);
        unknown.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-template-configuration");
        healthy.Status.Should().Be(TemplateResolutionStatus.Failed);
        healthy.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("item").And.Contain("template.json");
        await list.Should().ThrowAsync<InvalidOperationException>().WithMessage("*template.json*");
    }

    [Fact]
    public async Task ResolveAsync_ConfigurationCorruptedAfterListing_RejectsBothSelectionPaths()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();
        await File.WriteAllTextAsync(Path.Combine(packageDirectory, "item", ".template.config", "template.json"), "{ not valid json");

        TemplateResolutionResult byIdentity = await templater.ResolveAsync(entry.Identity, TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult byEntry = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);

        byIdentity.Status.Should().Be(TemplateResolutionStatus.Failed);
        byIdentity.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-template-configuration");
        byEntry.Status.Should().Be(TemplateResolutionStatus.Failed);
        byEntry.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-template-configuration");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_InstalledConfigurationWithoutShortName_ReportsInvalidConfiguration(bool removeShortName)
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        string configurationPath = Path.Combine(packageDirectory, "item", ".template.config", "template.json");
        System.Text.Json.Nodes.JsonObject configuration = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(configurationPath))!.AsObject();
        if (removeShortName)
        {
            configuration.Remove("shortName");
        }
        else
        {
            configuration["shortName"] = string.Empty;
        }

        await File.WriteAllTextAsync(configurationPath, configuration.ToJsonString());
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-template-configuration");
    }

    [Fact]
    public async Task ResolveAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using Templater templater = CreateTemplater();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => templater.ResolveAsync("item", TemplateType.Item, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveAsync_NullOrWhitespaceReference_Throws()
    {
        using Templater templater = CreateTemplater();
        Func<Task> nullReference = () => templater.ResolveAsync((string)null!, TemplateType.Item, CancellationToken.None);
        Func<Task> blankReference = () => templater.ResolveAsync(" ", TemplateType.Item, CancellationToken.None);
        Func<Task> nullEntry = () => templater.ResolveAsync((TemplateCatalogEntry)null!, TemplateType.Item, CancellationToken.None);

        await nullReference.Should().ThrowAsync<ArgumentNullException>().WithParameterName("reference");
        await blankReference.Should().ThrowAsync<ArgumentException>().WithParameterName("reference");
        await nullEntry.Should().ThrowAsync<ArgumentNullException>().WithParameterName("selectedEntry");
    }

    [Fact]
    public void Constructor_NullSession_Throws()
    {
        Action act = () => _ = new Templater(null!, ProjectStacks());

        act.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    [Fact]
    public void Constructor_NullProjectStacks_Throws()
    {
        using TemplateEngineSession session = new(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive);

        Action act = () => _ = new Templater(session, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("projectStacks");
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

    private Templater CreateTemplater(TemplateEngineContext? context = null, InstalledProjectStackCatalog? projectStacks = null)
        => new(
            new TemplateEngineSession(context ?? new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive),
            projectStacks ?? new InstalledProjectStackCatalog([]));

    private TemplateEngineContext ProjectContext(string? stack, string? language)
        => new(
            WorkingDirectory.FromExplicit(_root),
            new TemplateEngineProjectContext(WorkingDirectory.FromExplicit(_root), stack, language));

    private static InstalledProjectStackCatalog ProjectStacks() => new([new NodeProjectStack(), new PythonProjectStack()]);

    public sealed class SharedMountProviderFactory : ITemplatePackageProviderFactory
    {
        public Guid Id { get; } = new("5e2032cc-31eb-41b8-8ba0-77ff0b21b5b4");

        public string DisplayName => "Shared mount test provider";

        public ITemplatePackageProvider CreateProvider(IEngineEnvironmentSettings settings)
        {
            if (!settings.Host.TryGetHostParamDefault("WorkingDirectory", out string? root) || root is null)
            {
                throw new InvalidOperationException("The test provider requires a working directory.");
            }

            return new SharedMountProvider(this, Path.Combine(root, "basic-templates"));
        }
    }

    private sealed class SharedMountProvider(ITemplatePackageProviderFactory factory, string packageDirectory) : ITemplatePackageProvider
    {
        public event Action? TemplatePackagesChanged
        {
            add { }
            remove { }
        }

        public ITemplatePackageProviderFactory Factory => factory;

        public Task<IReadOnlyList<ITemplatePackage>> GetAllTemplatePackagesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ITemplatePackage> packages =
            [
                new TemplatePackage(this, packageDirectory, Directory.GetLastWriteTimeUtc(packageDirectory)),
            ];
            return Task.FromResult(packages);
        }
    }

    private sealed class NodeProjectStack : IProjectStack
    {
        public string Stack => "node";

        public IReadOnlyList<string> SupportedLanguages => ["JavaScript", "TypeScript"];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } = new Dictionary<string, IReadOnlyList<string>>
        {
            ["JavaScript"] = ["js"],
            ["TypeScript"] = ["ts"],
        };
    }

    private sealed class PythonProjectStack : IProjectStack
    {
        public string Stack => "python";

        public IReadOnlyList<string> SupportedLanguages => ["Python"];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Python"] = ["py"],
        };
    }
}
