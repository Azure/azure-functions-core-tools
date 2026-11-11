// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateSelectionTests : IDisposable
{
    private readonly string _root;
    private readonly string _hive;

    public TemplateSelectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-template-selection-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task ResolveAsync_ExactIdentityTakesPrecedenceOverAnotherTemplatesAlias()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("identity", "Func.Tests.Shared", "identity-alias", null, null, null, null),
            ("alias", "Func.Tests.Other", "Func.Tests.Shared", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync();

        TemplateResolutionResult result = await selection.ResolveAsync("func.tests.shared", TemplateType.Item);

        result.Candidates.Should().ContainSingle().Which.Identity.Should().Be("Func.Tests.Shared");
    }

    [Fact]
    public async Task ResolveAsync_AliasFiltersByRequestedTemplateKind()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackageForType(
            _root,
            "project",
            ("project", "Func.Tests.Project", "shared", null, null, null, null));
        TemplateFixtures.WriteSelectionPackage(
            _root,
            ("item", "Func.Tests.Item", "shared", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync();

        TemplateResolutionResult result = await selection.ResolveAsync("shared", TemplateType.Item);

        result.Candidates.Should().ContainSingle().Which.Identity.Should().Be("Func.Tests.Item");
    }

    [Fact]
    public async Task ResolveAsync_WrongKindExactIdentityDoesNotFallThroughToAlias()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackageForType(
            _root,
            "project",
            ("project", "Func.Tests.Requested", "project-alias", null, null, null, null));
        TemplateFixtures.WriteSelectionPackage(
            _root,
            ("item", "Func.Tests.Other", "Func.Tests.Requested", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync();

        TemplateResolutionResult result = await selection.ResolveAsync("Func.Tests.Requested", TemplateType.Item);

        result.Candidates.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("wrong-kind");
        result.Diagnostics.Single().Message.Should().Be(
            "Template 'Func.Tests.Requested' is not an item template.");
    }

    [Fact]
    public async Task ResolveAsync_WrongKindAlias_UsesCorrectArticle()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackageForType(
            _root,
            "project",
            ("project", "Func.Tests.Project", "project-alias", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync();

        TemplateResolutionResult result = await selection.ResolveAsync("project-alias", TemplateType.Item);

        result.Candidates.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Be(
            "Template alias 'project-alias' does not identify an item template.");
    }

    [Fact]
    public async Task ResolveAsync_ProjectStackAndLanguageDoNotFilterTemplateEligibility()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("javascript", "Func.Tests.JavaScript", "shared", null, "JavaScript", null, null),
            ("javascript2", "Func.Tests.JavaScript2", "shared", null, "JavaScript", null, null),
            ("python", "Func.Tests.Python", "shared", null, "Python", null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync(ProjectContext("node", "JavaScript"));

        TemplateResolutionResult result = await selection.ResolveAsync("shared", TemplateType.Item);

        result.Candidates.Should().HaveCountGreaterThan(1);
        result.Candidates.Select(candidate => candidate.Identity)
            .Should().BeEquivalentTo(["Func.Tests.JavaScript", "Func.Tests.JavaScript2", "Func.Tests.Python"]);
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("ambiguous");
    }

    [Fact]
    public async Task ResolveAsync_UnsupportedEngineConstraintDoesNotRejectHealthyAliasCandidate()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", "Func.Tests.Constrained", "shared", null, null, "future-constraint", null),
            ("healthy", "Func.Tests.Healthy", "shared", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync();

        TemplateResolutionResult result = await selection.ResolveAsync("shared", TemplateType.Item);

        result.Candidates.Should().ContainSingle().Which.Identity.Should().Be("Func.Tests.Healthy");
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-not-evaluated");
    }

    [Fact]
    public async Task ResolveAsync_DuplicateConstraintFactories_FailsClosed()
    {
        const string constraintType = "func-tests-duplicate-factory";
        const string identity = "Func.Tests.DuplicateConstraintFactory";
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", identity, "duplicate-factory", null, null, constraintType, new { }));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        DuplicateConstraintFactory firstFactory = new(Guid.NewGuid(), constraintType);
        DuplicateConstraintFactory secondFactory = new(Guid.NewGuid(), constraintType);
        using SelectionHarness firstOrder = await CreateSelectionAsync(
            additionalFactories: [firstFactory, secondFactory]);
        using SelectionHarness reverseOrder = await CreateSelectionAsync(
            additionalFactories: [secondFactory, firstFactory]);

        firstOrder.GetConstraintFactoryCount(constraintType).Should().Be(2);
        reverseOrder.GetConstraintFactoryCount(constraintType).Should().Be(2);

        TemplateResolutionResult firstResult = await firstOrder.ResolveAsync("duplicate-factory", TemplateType.Item);
        TemplateResolutionResult reverseResult = await reverseOrder.ResolveAsync("duplicate-factory", TemplateType.Item);

        foreach (TemplateResolutionResult result in new[] { firstResult, reverseResult })
        {
            result.Candidates.Should().BeEmpty();
            TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
            diagnostic.Code.Should().Be("constraint-not-evaluated");
            diagnostic.TemplateIdentity.Should().Be(identity);
            diagnostic.PackageIdentity.Should().Be(packageDirectory);
            diagnostic.TemplateName.Should().NotBeNullOrWhiteSpace();
            diagnostic.TemplateKind.Should().Be(TemplateType.Item);
            diagnostic.Message.Should().Contain(constraintType);
        }

        firstFactory.CreateCount.Should().Be(0);
        secondFactory.CreateCount.Should().Be(0);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        Func<Task> resolveCanceled = () => firstOrder.ResolveAsync(
            "duplicate-factory",
            TemplateType.Item,
            cancellation.Token);
        await resolveCanceled.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveAsync_RawConstraintMissingFromEngineMetadata_FailsClosed()
    {
        const string identity = "Func.Tests.RawConstraintMissingFromEngineMetadata";
        using var args = System.Text.Json.JsonDocument.Parse("""[{"hostname":"func"}]""");
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", identity, "constrained", null, null, "host", args.RootElement.Clone()));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using TemplateEngineSession session = new(
            new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            _hive);
        (IReadOnlyList<ITemplatePackage> packages, IReadOnlyList<ITemplateInfo> engineTemplates) =
            await session.GetInventoryAsync(CancellationToken.None);
        ITemplateInfo engineCandidate = engineTemplates.Single(template => template.Identity == identity);
        ITemplateInfo candidate = Substitute.For<ITemplateInfo>();
        candidate.Identity.Returns(engineCandidate.Identity);
        candidate.TagsCollection.Returns(engineCandidate.TagsCollection);
        candidate.ShortNameList.Returns(engineCandidate.ShortNameList);
        candidate.MountPointUri.Returns(engineCandidate.MountPointUri);
        candidate.ConfigPlace.Returns(engineCandidate.ConfigPlace);
        candidate.Constraints.Returns(Array.Empty<TemplateConstraintInfo>());

        TemplateCatalogEntry entry = TemplateCatalogProjector.Project(
            engineCandidate,
            packages.Single(package => package.MountPointUri == engineCandidate.MountPointUri));
        TemplateEligibilityEvaluator evaluator = new(
            session,
            new(new TemplateConstraintRequirementMatcher()));

        TemplateEligibilityResult result = await evaluator.EvaluateAsync(candidate, entry, CancellationToken.None);

        result.IsEligible.Should().BeFalse();
        TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("constraint-metadata-mismatch");
        diagnostic.TemplateIdentity.Should().Be(identity);
        diagnostic.PackageIdentity.Should().Be(packageDirectory);
    }

    [Fact]
    public async Task ResolveAsync_MalformedRawConstraintRetainsMetadataCause()
    {
        const string identity = "Func.Tests.MalformedRawConstraint";
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", identity, "malformed-raw", null, null, "host", new { hostname = "func" }));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync();
        File.WriteAllText(
            Path.Combine(packageDirectory, "constrained", ".template.config", "template.json"),
            "{ not json");

        TemplateResolutionResult result = await selection.ResolveAsync("malformed-raw", TemplateType.Item);

        result.Candidates.Should().BeEmpty();
        TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("invalid-constraints");
        diagnostic.Cause.Should().BeOfType<InvalidTemplateMetadataException>()
            .Which.InnerException.Should().BeAssignableTo<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task ResolveAsync_SourceReadFailureRetainsCause()
    {
        const string identity = "Func.Tests.SourceReadFailure";
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", identity, "source-read-failure", null, null, "host", new { hostname = "func" }));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync();
        string configurationPath = Path.Combine(packageDirectory, "constrained", ".template.config", "template.json");
        using FileStream exclusiveHandle = new(configurationPath, FileMode.Open, FileAccess.Read, FileShare.None);

        TemplateResolutionResult result = await selection.ResolveAsync("source-read-failure", TemplateType.Item);

        result.Candidates.Should().BeEmpty();
        TemplateResolutionDiagnostic diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("constraint-source-unavailable");
        diagnostic.Cause.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task ResolveAsync_ConstraintMetadataTypeMismatchFailsClosed()
    {
        const string identity = "Func.Tests.ConstraintMetadataTypeMismatch";
        using var args = System.Text.Json.JsonDocument.Parse("""[{"hostname":"func"}]""");
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", identity, "type-mismatch", null, null, "host", args.RootElement.Clone()));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using TemplateEngineSession session = new(
            new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            _hive);
        (IReadOnlyList<ITemplatePackage> packages, IReadOnlyList<ITemplateInfo> engineTemplates) =
            await session.GetInventoryAsync(CancellationToken.None);
        ITemplateInfo engineCandidate = engineTemplates.Single(template => template.Identity == identity);
        ITemplateInfo candidate = Substitute.For<ITemplateInfo>();
        candidate.Identity.Returns(engineCandidate.Identity);
        candidate.TagsCollection.Returns(engineCandidate.TagsCollection);
        candidate.ShortNameList.Returns(engineCandidate.ShortNameList);
        candidate.MountPointUri.Returns(engineCandidate.MountPointUri);
        candidate.ConfigPlace.Returns(engineCandidate.ConfigPlace);
        candidate.Constraints.Returns([new("different-type", args.RootElement.GetRawText())]);
        TemplateCatalogEntry entry = TemplateCatalogProjector.Project(
            engineCandidate,
            packages.Single(package => package.MountPointUri == engineCandidate.MountPointUri));
        TemplateEligibilityEvaluator evaluator = new(
            session,
            new(new TemplateConstraintRequirementMatcher()));

        TemplateEligibilityResult result = await evaluator.EvaluateAsync(candidate, entry, CancellationToken.None);

        result.IsEligible.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-metadata-mismatch");
    }

    [Fact]
    public async Task ResolveAsync_NoRawOrEngineConstraints_RemainsEligible()
    {
        const string identity = "Func.Tests.NoConstraints";
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("unconstrained", identity, "unconstrained", null, null, null, null));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        DuplicateConstraintFactory unrelatedFactory = new(Guid.NewGuid(), "func-tests-unused");
        using SelectionHarness selection = await CreateSelectionAsync(
            additionalFactories: [unrelatedFactory]);

        TemplateResolutionResult result = await selection.ResolveAsync("unconstrained", TemplateType.Item);

        selection.HasConstraintManager.Should().BeFalse();
        result.Candidates.Should().ContainSingle().Which.Identity.Should().Be(identity);
        result.Diagnostics.Should().BeEmpty();
        unrelatedFactory.CreateCount.Should().Be(0);
        selection.HasConstraintManager.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_UsesEngineConstraintManagerForSupportedConstraints()
    {
        using var args = System.Text.Json.JsonDocument.Parse("""[{"hostname":"func"}]""");
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("host", "Func.Tests.Host", "host", null, null, "host", args.RootElement.Clone()));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync();

        TemplateResolutionResult result = await selection.ResolveAsync("host", TemplateType.Item);

        result.Candidates.Should().ContainSingle().Which.Identity.Should().Be("Func.Tests.Host");
    }

    [Fact]
    public async Task ResolveAsync_FuncBundleConstraintUsesTheResolvedBundleContext()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("bundle", "Func.Tests.Bundle", "bundle", null, null, "func-bundle", new { id = "test.bundle", version = "[4.20.0,5.0.0)" }));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync(
            new(
                WorkingDirectory.FromExplicit(_root),
                bundle: new TemplateEngineBundleContext("test.bundle", "4.25.0")));

        TemplateResolutionResult result = await selection.ResolveAsync("bundle", TemplateType.Item);

        result.Candidates.Should().ContainSingle().Which.Identity.Should().Be("Func.Tests.Bundle");
    }

    [Fact]
    public async Task ResolveAsync_RestrictedBundleConstraintRejectsCandidate()
    {
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("bundle", "Func.Tests.RestrictedBundle", "restricted-bundle", null, null, "func-bundle", new { id = "test.bundle", version = "[4.20.0,5.0.0)" }));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync(
            new(
                WorkingDirectory.FromExplicit(_root),
                bundle: new TemplateEngineBundleContext("test.bundle", "3.0.0")));

        TemplateResolutionResult result = await selection.ResolveAsync("restricted-bundle", TemplateType.Item);

        result.Candidates.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-restricted");
    }

    [Fact]
    public async Task ResolveAsync_EngineRestrictedResultRejectsCandidate()
    {
        const string constraintType = "func-tests-restricted";
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", "Func.Tests.EngineRestricted", "engine-restricted", null, null, constraintType, new { }));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using SelectionHarness selection = await CreateSelectionAsync(
            additionalFactories: [new RestrictedConstraintFactory(constraintType)]);

        TemplateResolutionResult result = await selection.ResolveAsync("engine-restricted", TemplateType.Item);

        result.Candidates.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("constraint-restricted");
    }

    [Fact]
    public async Task ResolveAsync_CancellationAfterConstraintEvaluationStartsPropagates()
    {
        const string constraintType = "func-tests-blocking";
        string packageDirectory = TemplateFixtures.WriteSelectionPackage(
            _root,
            ("constrained", "Func.Tests.BlockingConstraint", "blocking", null, null, constraintType, new { }));
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        BlockingConstraintFactory factory = new(constraintType);
        using SelectionHarness selection = await CreateSelectionAsync(additionalFactories: [factory]);
        using CancellationTokenSource cancellation = new();

        Task<TemplateResolutionResult> resolution = selection.ResolveAsync(
            "blocking",
            TemplateType.Item,
            cancellation.Token);
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => resolution).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void EligibilityResult_RequiresCandidateScopedRejectionDiagnostics()
    {
        var candidate = new TemplateCatalogEntry(
            identity: "Func.Tests.Hidden",
            name: "Hidden",
            description: null,
            type: TemplateType.Item,
            shortNames: ["hidden"],
            groupIdentity: null,
            language: null,
            precedence: 0,
            package: null,
            isHidden: false,
            parameters: [],
            metadataDiagnostic: null);
        var diagnostic = new TemplateResolutionDiagnostic("hidden", "The candidate is hidden.", candidate.Identity);

        var accepted = TemplateEligibilityResult.Eligible;
        var rejected = TemplateEligibilityResult.Rejected(candidate, diagnostic);

        accepted.IsEligible.Should().BeTrue();
        accepted.Diagnostics.Should().BeEmpty();
        rejected.IsEligible.Should().BeFalse();
        rejected.Diagnostics.Should().ContainSingle().Which.Should().BeSameAs(diagnostic);
        Action unrelatedDiagnostic = () => TemplateEligibilityResult.Rejected(
            candidate,
            new("hidden", "The diagnostic belongs to another candidate.", "Func.Tests.Other"));
        unrelatedDiagnostic.Should().Throw<ArgumentException>();
    }

    private async Task<SelectionHarness> CreateSelectionAsync(
        TemplateEngineContext? context = null,
        params ITemplateConstraintFactory[] additionalFactories)
    {
        TemplateEngineSession session = new(
            context ?? new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            _hive);
        try
        {
            foreach (ITemplateConstraintFactory factory in additionalFactories)
            {
                session.Settings.Components.AddComponent(typeof(ITemplateConstraintFactory), factory);
            }

            (IReadOnlyList<ITemplatePackage> packages, IReadOnlyList<ITemplateInfo> engineTemplates) =
                await session.GetInventoryAsync(CancellationToken.None);
            Dictionary<string, ITemplatePackage> packagesByMountPoint = new(StringComparer.Ordinal);
            foreach (ITemplatePackage package in packages)
            {
                packagesByMountPoint.TryAdd(package.MountPointUri, package);
            }

            List<TemplateCatalogEntry> entries = [];
            foreach (ITemplateInfo template in engineTemplates)
            {
                if (TemplateCatalogProjector.GetTemplateType(template) is null)
                {
                    continue;
                }

                TemplateCatalogEntry entry = TemplateCatalogProjector.Project(
                    template,
                    packagesByMountPoint.GetValueOrDefault(template.MountPointUri),
                    session.OwnershipToken);
                entries.Add(entry);
            }

            TemplateSelection selector = new(
                session,
                new TemplateEligibilityEvaluator(session, new(new TemplateConstraintRequirementMatcher())));
            return new(session, selector, entries);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private TemplateEngineContext ProjectContext(string? stack, string? language)
        => new(
            WorkingDirectory.FromExplicit(_root),
            new TemplateEngineProjectContext(WorkingDirectory.FromExplicit(_root), stack, language));

    private sealed class SelectionHarness(
        TemplateEngineSession session,
        TemplateSelection selection,
        IReadOnlyList<TemplateCatalogEntry> entries) : IDisposable
    {
        public void Dispose() => session.Dispose();

        public bool HasConstraintManager
            => typeof(TemplateEngineSession)
                .GetField("_constraintManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(session) is not null;

        public int GetConstraintFactoryCount(string type)
            => session.Settings.Components.OfType<ITemplateConstraintFactory>().Count(factory => factory.Type == type);

        public Task<TemplateResolutionResult> ResolveAsync(
            string reference,
            TemplateType type,
            CancellationToken cancellationToken = default)
            => selection.ResolveAsync(entries, reference, type, cancellationToken);
    }

    private sealed class DuplicateConstraintFactory(Guid id, string type) : ITemplateConstraintFactory
    {
        public Guid Id { get; } = id;

        public string Type { get; } = type;

        public int CreateCount { get; private set; }

        public Task<ITemplateConstraint> CreateTemplateConstraintAsync(
            IEngineEnvironmentSettings environmentSettings,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            return Task.FromResult<ITemplateConstraint>(new DuplicateFactoryConstraint(Type));
        }
    }

    private sealed class DuplicateFactoryConstraint(string type) : ITemplateConstraint
    {
        public string Type { get; } = type;

        public string DisplayName => "Duplicate factory test constraint";

        public TemplateConstraintResult Evaluate(string? args) => TemplateConstraintResult.CreateAllowed(this);
    }

    private sealed class BlockingConstraintFactory(string type) : ITemplateConstraintFactory
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Type { get; } = type;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ITemplateConstraint> CreateTemplateConstraintAsync(
            IEngineEnvironmentSettings environmentSettings,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new DuplicateFactoryConstraint(Type);
        }
    }

    private sealed class RestrictedConstraintFactory(string type) : ITemplateConstraintFactory
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Type { get; } = type;

        public Task<ITemplateConstraint> CreateTemplateConstraintAsync(
            IEngineEnvironmentSettings environmentSettings,
            CancellationToken cancellationToken)
            => Task.FromResult<ITemplateConstraint>(new RestrictedConstraint(Type));
    }

    private sealed class RestrictedConstraint(string type) : ITemplateConstraint
    {
        public string Type { get; } = type;

        public string DisplayName => "Restricted test constraint";

        public TemplateConstraintResult Evaluate(string? args)
            => TemplateConstraintResult.CreateRestricted(this, "The test constraint is restricted.", "Update the test environment.");
    }
}
