// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateResolutionResultTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-template-result-tests-" + Guid.NewGuid().ToString("N"));
    private string Hive => Path.Combine(_root, "hive");

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
    public async Task Constructor_AcceptsEachValidStatusPayload()
    {
        (TemplateEngineSession session, ResolvedTemplate selection, TemplateCatalogEntry[] entries) = await CreateInputsAsync();
        using (session)
        {
            TemplateResolutionDiagnostic diagnostic = new("warning", "Candidate was rejected.", entries[0].Identity, "package", new InvalidOperationException());

            TemplateResolutionResult selected = new(TemplateResolutionStatus.Selected, selection, [], [diagnostic]);
            TemplateResolutionResult notFound = new(TemplateResolutionStatus.NotFound, null, [], []);
            TemplateResolutionResult ambiguous = new(TemplateResolutionStatus.Ambiguous, null, entries, [diagnostic]);
            TemplateResolutionResult failed = new(TemplateResolutionStatus.Failed, null, [entries[0]], [diagnostic]);

            selected.SelectedTemplate.Should().BeSameAs(selection);
            selected.Candidates.Should().BeEmpty();
            selected.Diagnostics.Should().ContainSingle().Which.Should().BeSameAs(diagnostic);
            notFound.SelectedTemplate.Should().BeNull();
            notFound.Candidates.Should().BeEmpty();
            ambiguous.Candidates.Should().HaveCount(2);
            failed.Candidates.Should().ContainSingle().Which.Should().Be(entries[0]);
            failed.Diagnostics.Single().Cause.Should().BeSameAs(diagnostic.Cause);
        }
    }

    [Fact]
    public async Task Constructor_RejectsInvalidStatusPayloads()
    {
        (TemplateEngineSession session, ResolvedTemplate selection, TemplateCatalogEntry[] entries) = await CreateInputsAsync();
        using (session)
        {
            TemplateResolutionDiagnostic diagnostic = new("failed", "Resolution failed.");

            Assert.Throws<ArgumentException>(() => new TemplateResolutionResult(TemplateResolutionStatus.Selected, null, [], []));
            Assert.Throws<ArgumentException>(() => new TemplateResolutionResult(TemplateResolutionStatus.Selected, selection, [entries[0]], []));
            Assert.Throws<ArgumentException>(() => new TemplateResolutionResult(TemplateResolutionStatus.NotFound, selection, [], []));
            Assert.Throws<ArgumentException>(() => new TemplateResolutionResult(TemplateResolutionStatus.NotFound, null, [entries[0]], []));
            Assert.Throws<ArgumentException>(() => new TemplateResolutionResult(TemplateResolutionStatus.Ambiguous, selection, entries, [diagnostic]));
            Assert.Throws<ArgumentException>(() => new TemplateResolutionResult(TemplateResolutionStatus.Ambiguous, null, [entries[0]], [diagnostic]));
            Assert.Throws<ArgumentException>(() => new TemplateResolutionResult(TemplateResolutionStatus.Failed, selection, [], [diagnostic]));
            Assert.Throws<ArgumentException>(() => new TemplateResolutionResult(TemplateResolutionStatus.Failed, null, [], []));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemplateResolutionResult((TemplateResolutionStatus)int.MaxValue, null, [], []));
        }
    }

    [Fact]
    public async Task Constructor_DefensivelyCopiesAndExposesReadOnlyCollections()
    {
        (TemplateEngineSession session, _, TemplateCatalogEntry[] entries) = await CreateInputsAsync();
        using (session)
        {
            List<TemplateCatalogEntry> candidates = [.. entries];
            List<TemplateResolutionDiagnostic> diagnostics = [new("ambiguous", "Choose a candidate.")];
            TemplateResolutionResult result = new(TemplateResolutionStatus.Ambiguous, null, candidates, diagnostics);
            candidates.Clear();
            diagnostics.Clear();

            result.Candidates.Should().HaveCount(2);
            result.Diagnostics.Should().ContainSingle();
            Action addCandidate = () => ((IList<TemplateCatalogEntry>)result.Candidates).Add(entries[0]);
            Action clearDiagnostics = () => ((IList<TemplateResolutionDiagnostic>)result.Diagnostics).Clear();

            addCandidate.Should().Throw<NotSupportedException>();
            clearDiagnostics.Should().Throw<NotSupportedException>();
        }
    }

    [Fact]
    public void Constructor_RejectsNullCollections()
    {
        Action nullCandidates = () => new TemplateResolutionResult(TemplateResolutionStatus.NotFound, null, null!, []);
        Action nullDiagnostics = () => new TemplateResolutionResult(TemplateResolutionStatus.NotFound, null, [], null!);

        nullCandidates.Should().Throw<ArgumentNullException>().WithParameterName("candidates");
        nullDiagnostics.Should().Throw<ArgumentNullException>().WithParameterName("diagnostics");
    }

    private async Task<(TemplateEngineSession Session, ResolvedTemplate Selection, TemplateCatalogEntry[] Entries)> CreateInputsAsync()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, Hive);
        TemplateEngineSession session = new(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), Hive);
        IReadOnlyList<ITemplateInfo> templates = (await session.GetInventoryAsync(CancellationToken.None)).Templates;
        ITemplateInfo engineTemplate = templates.Single(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
        TemplateCatalogEntry[] entries =
        [
            TemplateCatalogProjector.Project(engineTemplate, package: null),
            TemplateCatalogProjector.Project(
                templates.Single(template => template.Identity == TemplateFixtures.ProjectTemplateIdentity),
                package: null),
        ];
        ResolvedTemplate selection = new(session, entries[0], engineTemplate);
        return (session, selection, entries);
    }
}
