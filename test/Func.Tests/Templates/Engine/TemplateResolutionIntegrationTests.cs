// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateResolutionIntegrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _hive;

    public TemplateResolutionIntegrationTests()
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
            identity: TemplateFixtures.ItemTemplateIdentity,
            name: "Func Tests Item",
            description: null,
            type: TemplateType.Item,
            shortNames: [TemplateFixtures.ItemTemplateShortName],
            groupIdentity: null,
            language: null,
            precedence: 0,
            package: new TemplatePackageInfo(packageDirectory, Version: null),
            isHidden: false,
            parameters: [],
            metadataDiagnostic: null));
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
    public async Task ResolveAsync_ReusesOneInventoryAndRetainsTheExactEngineCandidate()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        TemplateEngineSession session = new(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive);
        (IReadOnlyList<ITemplatePackage> packages, IReadOnlyList<ITemplateInfo> templates) =
            await session.GetInventoryAsync(CancellationToken.None);
        ITemplateInfo engineCandidate = templates.Single(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
        using Templater templater = CreateTemplater(session);

        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();
        TemplateResolutionResult byAlias = await templater.ResolveAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult byEntry = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);
        (IReadOnlyList<ITemplatePackage> cachedPackages, IReadOnlyList<ITemplateInfo> cachedTemplates) =
            await session.GetInventoryAsync(CancellationToken.None);

        cachedPackages.Should().BeSameAs(packages);
        cachedTemplates.Should().BeSameAs(templates);
        byAlias.Status.Should().Be(TemplateResolutionStatus.Selected);
        byEntry.Status.Should().Be(TemplateResolutionStatus.Selected);
        byAlias.SelectedTemplate!.Template.Should().BeSameAs(engineCandidate);
        byEntry.SelectedTemplate!.Template.Should().BeSameAs(engineCandidate);
        byAlias.Candidates.Should().BeEmpty();
        byEntry.Candidates.Should().BeEmpty();
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
    public async Task ResolveAsync_AfterSessionDisposalDoesNotBecomeNotFound()
    {
        TemplateEngineSession session = new(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater templater = CreateTemplater(session);
        session.Dispose();

        Func<Task> act = () => templater.ResolveAsync("not-installed", TemplateType.Item, CancellationToken.None);

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
        exact.Candidates.Should().BeEmpty();
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
    public async Task ResolveAsync_IneligibleExactIdentity_DoesNotFallThroughToAlias()
    {
        const string exactIdentity = "Contoso.Http";
        const string aliasIdentity = "Contoso.Http.Alternative";
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("exact", exactIdentity, "exact", null, null, "future-constraint", new { }),
            ("alias", aliasIdentity, exactIdentity, null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        TemplateEngineSession session = new(
            new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            _hive);
        (IReadOnlyList<ITemplatePackage> packages, IReadOnlyList<ITemplateInfo> templates) =
            await session.GetInventoryAsync(CancellationToken.None);
        ITemplateInfo exactCandidate = templates.Single(template => template.Identity == exactIdentity);
        ITemplateInfo aliasCandidate = templates.Single(template => template.Identity == aliasIdentity);
        using Templater templater = CreateTemplater(session);

        TemplateResolutionResult result = await templater.ResolveAsync(exactIdentity, TemplateType.Item, CancellationToken.None);
        (IReadOnlyList<ITemplatePackage> cachedPackages, IReadOnlyList<ITemplateInfo> cachedTemplates) =
            await session.GetInventoryAsync(CancellationToken.None);
        ITemplatePackage aliasPackage = packages.Single(package => package.MountPointUri == aliasCandidate.MountPointUri);
        TemplateCatalogEntry aliasEntry = TemplateCatalogProjector.Project(aliasCandidate, aliasPackage);
        TemplateEligibilityResult aliasEligibility = await new TemplateEligibilityEvaluator(
            session,
            FuncConstraintEvaluator()).EvaluateAsync(aliasCandidate, aliasEntry, CancellationToken.None);

        exactCandidate.Identity.Should().Be(exactIdentity);
        aliasCandidate.ShortNameList.Should().Contain(exactIdentity);
        aliasEligibility.IsEligible.Should().BeTrue();
        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.SelectedTemplate.Should().BeNull();
        result.Candidates.Should().ContainSingle().Which.Identity.Should().Be(exactIdentity);
        TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("constraint-not-evaluated");
        diagnostic.TemplateIdentity.Should().Be(exactIdentity);
        diagnostic.TemplateIdentity.Should().NotBe(aliasIdentity);
        cachedPackages.Should().BeSameAs(packages);
        cachedTemplates.Should().BeSameAs(templates);
        cachedTemplates.Should().Contain(candidate => ReferenceEquals(candidate, exactCandidate));
        cachedTemplates.Should().Contain(candidate => ReferenceEquals(candidate, aliasCandidate));
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
        result.Candidates.Should().BeEmpty();
        result.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.First");
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
        using Templater templater = CreateTemplater(session);
        (IReadOnlyList<ITemplatePackage> packages, IReadOnlyList<ITemplateInfo> templates) =
            await session.GetInventoryAsync(CancellationToken.None);
        packages.Count(package => package.MountPointUri == packageDirectory).Should().Be(2);
        templates.Should().ContainSingle(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);

        TemplateResolutionResult byIdentity = await templater.ResolveAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);
        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();
        TemplateResolutionResult byEntry = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);
        (IReadOnlyList<ITemplatePackage> cachedPackages, IReadOnlyList<ITemplateInfo> cachedTemplates) =
            await session.GetInventoryAsync(CancellationToken.None);

        cachedPackages.Should().BeSameAs(packages);
        cachedTemplates.Should().BeSameAs(templates);
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
        TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("duplicate-identity");
        diagnostic.TemplateIdentity.Should().Be("FUNC.TESTS.DUPLICATE");
        diagnostic.Message.Should().Contain("first").And.Contain("second");
    }

    [Fact]
    public async Task ResolveAsync_AliasOfDuplicateIdentity_FailsInsteadOfChoosingOnePackage()
    {
        string first = TemplateFixtures.WriteSelectionPackage(
            Path.Combine(_root, "first"),
            ("one", "Func.Tests.Duplicate", "shared", null, null, null, null));
        string second = TemplateFixtures.WriteSelectionPackage(
            Path.Combine(_root, "second"),
            ("two", "func.tests.duplicate", "shared", null, null, null, null));
        await TemplateFixtures.InstallAsync(first, _hive);
        await TemplateFixtures.InstallAsync(second, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("shared", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.SelectedTemplate.Should().BeNull();
        TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("duplicate-identity");
        diagnostic.TemplateIdentity.Should().Be("Func.Tests.Duplicate");
        result.Status.Should().NotBe(TemplateResolutionStatus.NotFound);
    }

    [Fact]
    public async Task ResolveAsync_DuplicateIdentityWithinPackage_Fails()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("one", "Func.Tests.Duplicate", "first", null, null, null, null),
            ("two", "func.tests.duplicate", "second", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("Func.Tests.Duplicate", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.SelectedTemplate.Should().BeNull();
        TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("duplicate-identity");
        diagnostic.TemplateIdentity.Should().Be("Func.Tests.Duplicate");
    }

    [Fact]
    public async Task ResolveAsync_DoesNotApplyDeferredStackOrLanguageCompatibility()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("python", "Func.Tests.Python", "shared", null, "Python", null, null),
            ("javascript", "Func.Tests.JavaScript", "shared", null, "JavaScript", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater(context: ProjectContext("node", "JavaScript"));

        TemplateResolutionResult result = await templater.ResolveAsync("shared", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Ambiguous);
        result.Candidates.Select(candidate => candidate.Identity)
            .Should().BeEquivalentTo(["Func.Tests.Python", "Func.Tests.JavaScript"]);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("ambiguous");
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

    [Theory]
    [InlineData("sdk-version", "10.0.100", "Func.Tests.Sdk")]
    [InlineData("workload", "wasm-tools", "Func.Tests.Workload")]
    public async Task ResolveAsync_UnavailableEngineConstraintProviderDoesNotRejectHealthyAliasCandidate(
        string constraintType,
        string constraintArgument,
        string constrainedIdentity)
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", constrainedIdentity, "shared", null, null, constraintType, constraintArgument),
            ("healthy", "Func.Tests.Healthy", "shared", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("shared", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Selected);
        result.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.Healthy");
        TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("constraint-not-evaluated");
        diagnostic.TemplateIdentity.Should().Be(constrainedIdentity);
        diagnostic.PackageIdentity.Should().Be(packageDirectory);
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
    public async Task ResolveAsync_HostConstraintUsesTemplateEngineManager()
    {
        using var args = System.Text.Json.JsonDocument.Parse("""[{"hostname":"func"}]""");
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("host", "Func.Tests.Host", "host", null, null, "host", args.RootElement.Clone()));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ResolveAsync("host", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Selected);
        result.SelectedTemplate!.Descriptor.Identity.Should().Be("Func.Tests.Host");
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
        selection.Template.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
        copied.Status.Should().Be(TemplateResolutionStatus.Failed);
        copied.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-selection");
        foreignEntry.Status.Should().Be(TemplateResolutionStatus.Failed);
        foreignEntry.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-selection");
        foreignResolvedEntry.Status.Should().Be(TemplateResolutionStatus.Failed);
        foreignResolvedEntry.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-selection");

        owner.Dispose();
        selection.Descriptor.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
        Action accessDisposedCandidate = () => _ = selection.Template;
        Func<Task> resolveDisposed = () => owner.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);
        Func<Task> resolveSelectionDisposed = () => owner.ResolveAsync(selection.Descriptor, TemplateType.Item, CancellationToken.None);
        accessDisposedCandidate.Should().Throw<ObjectDisposedException>();
        await resolveDisposed.Should().ThrowAsync<ObjectDisposedException>();
        await resolveSelectionDisposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ResolveAsync_CorruptConfigurationIsOmittedByEngineInventory()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        string configurationPath = Path.Combine(packageDirectory, "item", ".template.config", "template.json");
        await File.WriteAllTextAsync(configurationPath, "{ not valid json");
        using Templater templater = CreateTemplater();

        TemplateResolutionResult unknown = await templater.ResolveAsync("not-installed", TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult healthy = await templater.ResolveAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);

        unknown.Status.Should().Be(TemplateResolutionStatus.NotFound);
        healthy.Status.Should().Be(TemplateResolutionStatus.Selected);
    }

    [Fact]
    public async Task ResolveAsync_MissingConstraintConfigurationAfterInventoryReturnsDiagnostic()
    {
        using var args = System.Text.Json.JsonDocument.Parse("""[{"hostname":"func"}]""");
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("host", "Func.Tests.Host", "host", null, null, "host", args.RootElement.Clone()));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        TemplateCatalogEntry entry = (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Single();
        string configPath = Path.Combine(packageDirectory, "host", ".template.config", "template.json");

        // Fault-inject a missing constraint file after inventory creation; production registrations are immutable in-session.
        File.Delete(configPath);
        TemplateResolutionResult result = await templater.ResolveAsync(entry, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Failed);
        result.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be("constraint-source-unavailable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ResolveAsync_TemplateWithoutNonEmptyShortNameCannotBeSelected(string? shortName)
    {
        string packageDirectory = Path.Combine(_root, "invalid-short-name");
        string configDirectory = Path.Combine(packageDirectory, "template", ".template.config");
        Directory.CreateDirectory(configDirectory);
        System.Text.Json.Nodes.JsonObject configuration = new()
        {
            ["$schema"] = "http://json.schemastore.org/template",
            ["identity"] = "Func.Tests.MissingShortName",
            ["name"] = "Missing short name",
            ["tags"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "item" },
        };
        if (shortName is not null)
        {
            configuration["shortName"] = shortName;
        }

        await File.WriteAllTextAsync(Path.Combine(configDirectory, "template.json"), configuration.ToJsonString());
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        TemplateResolutionResult result = await templater.ResolveAsync("Func.Tests.MissingShortName", TemplateType.Item, CancellationToken.None);

        if (shortName is null)
        {
            entries.Should().NotContain(entry => entry.Identity == "Func.Tests.MissingShortName");
            result.Status.Should().Be(TemplateResolutionStatus.NotFound);
        }
        else
        {
            entries.Should().ContainSingle(entry => entry.Identity == "Func.Tests.MissingShortName")
                .Which.MetadataDiagnostic.Should().Contain("non-empty shortName");
            result.Status.Should().Be(TemplateResolutionStatus.Failed);
            result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("invalid-metadata");
        }
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
        Action act = () => _ = new Templater(null!, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    [Fact]
    public void Constructor_NullSelection_Throws()
    {
        using TemplateEngineSession session = new(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive);

        Action act = () => _ = new Templater(session, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("selection");
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

    private Templater CreateTemplater(TemplateEngineContext? context = null)
    {
        TemplateEngineSession session = new(context ?? new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive);
        return CreateTemplater(session);
    }

    private Templater CreateTemplater(TemplateEngineSession session)
        => new(
            session,
            new TemplateSelection(
                session,
                new TemplateEligibilityEvaluator(session, FuncConstraintEvaluator())));

    private static Lazy<FuncTemplateConstraintEvaluator> FuncConstraintEvaluator()
        => new(() => new(new TemplateConstraintRequirementMatcher()));

    private TemplateEngineContext ProjectContext(string? stack, string? language)
        => new(
            WorkingDirectory.FromExplicit(_root),
            new TemplateEngineProjectContext(WorkingDirectory.FromExplicit(_root), stack, language));

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

}
