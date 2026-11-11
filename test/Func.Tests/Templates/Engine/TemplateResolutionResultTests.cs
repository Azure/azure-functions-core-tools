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
    public async Task Factories_AcceptEachValidStatusPayload()
    {
        (TemplateEngineSession session, ResolvedTemplate selection, TemplateCatalogEntry[] entries) = await CreateInputsAsync();
        using (session)
        {
            var diagnostic = new TemplateResolutionDiagnostic("warning", "Candidate was rejected.", entries[0].Identity, "package", new InvalidOperationException());

            var selected = TemplateResolutionResult.Selected(selection, [diagnostic]);
            var notFound = TemplateResolutionResult.NotFound();
            var ambiguous = TemplateResolutionResult.Ambiguous(entries, [diagnostic]);
            var failed = TemplateResolutionResult.Failed([diagnostic], [entries[0]]);

            selected.SelectedTemplate.Should().BeSameAs(selection);
            selected.Candidates.Should().BeEmpty();
            selected.Diagnostics.Should().ContainSingle().Which.Should().BeSameAs(diagnostic);
            notFound.SelectedTemplate.Should().BeNull();
            notFound.Candidates.Should().BeEmpty();
            ambiguous.Candidates.Should().Equal(entries);
            failed.Candidates.Should().ContainSingle().Which.Should().BeSameAs(entries[0]);
            failed.Diagnostics.Single().Cause.Should().BeSameAs(diagnostic.Cause);
        }
    }

    [Fact]
    public async Task Factories_RejectInvalidStatusPayloads()
    {
        (TemplateEngineSession session, ResolvedTemplate selection, TemplateCatalogEntry[] entries) = await CreateInputsAsync();
        using (session)
        {
            var diagnostic = new TemplateResolutionDiagnostic("failed", "Resolution failed.");

            Assert.Throws<ArgumentNullException>(() => TemplateResolutionResult.Selected(null!));
            Assert.Throws<ArgumentException>(() => TemplateResolutionResult.Ambiguous([entries[0]], []));
            Assert.Throws<ArgumentException>(() => TemplateResolutionResult.Failed([]));
        }
    }

    [Fact]
    public async Task Factories_SnapshotCallerOwnedCollections()
    {
        (TemplateEngineSession session, _, TemplateCatalogEntry[] entries) = await CreateInputsAsync();
        using (session)
        {
            List<TemplateCatalogEntry> candidates = [.. entries];
            List<TemplateResolutionDiagnostic> diagnostics = [new("ambiguous", "Choose a candidate.")];
            var result = TemplateResolutionResult.Ambiguous(candidates, diagnostics);
            candidates.Clear();
            diagnostics.Clear();

            result.Candidates.Should().Equal(entries);
            result.Diagnostics.Should().ContainSingle();
        }
    }

    [Fact]
    public void Factories_RejectNullCollections()
    {
        Action nullAmbiguousCandidates = () => TemplateResolutionResult.Ambiguous(null!, []);
        Action nullAmbiguousDiagnostics = () => TemplateResolutionResult.Ambiguous([], null!);
        Action nullFailedDiagnostics = () => TemplateResolutionResult.Failed(null!);

        nullAmbiguousCandidates.Should().Throw<ArgumentNullException>().WithParameterName("candidates");
        nullAmbiguousDiagnostics.Should().Throw<ArgumentNullException>().WithParameterName("diagnostics");
        nullFailedDiagnostics.Should().Throw<ArgumentNullException>().WithParameterName("diagnostics");
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
