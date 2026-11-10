// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-templater-tests-" + Guid.NewGuid().ToString("N"));
    private string Hive => Path.Combine(_root, "hive");

    [Fact]
    public async Task ResolveAsync_UsesOneSessionInventoryAndRetainsItsExactCandidate()
    {
        Directory.CreateDirectory(Hive);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), Hive);
        TemplateEngineSession session = new(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), Hive);
        (IReadOnlyList<Microsoft.TemplateEngine.Abstractions.TemplatePackage.ITemplatePackage> packages,
            IReadOnlyList<ITemplateInfo> templates) = await session.GetInventoryAsync(CancellationToken.None);
        ITemplateInfo engineCandidate = templates.Single(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
        TemplateSelection selection = new(
            session,
            new TemplateEligibilityEvaluator(session, new(() => new(new TemplateConstraintRequirementMatcher()))));
        using Templater templater = new(session, selection);

        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Item);
        TemplateResolutionResult byAlias = await templater.ResolveAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item);
        TemplateResolutionResult byEntry = await templater.ResolveAsync(entries.Single(), TemplateType.Item);
        (IReadOnlyList<Microsoft.TemplateEngine.Abstractions.TemplatePackage.ITemplatePackage> cachedPackages,
            IReadOnlyList<ITemplateInfo> cachedTemplates) = await session.GetInventoryAsync(CancellationToken.None);

        cachedPackages.Should().BeSameAs(packages);
        cachedTemplates.Should().BeSameAs(templates);
        Action mutatePackages = () => ((IList<Microsoft.TemplateEngine.Abstractions.TemplatePackage.ITemplatePackage>)packages).Clear();
        Action mutateTemplates = () => ((IList<ITemplateInfo>)templates).Clear();
        mutatePackages.Should().Throw<NotSupportedException>();
        mutateTemplates.Should().Throw<NotSupportedException>();
        entries.Should().ContainSingle();
        byAlias.Status.Should().Be(TemplateResolutionStatus.Selected);
        byEntry.Status.Should().Be(TemplateResolutionStatus.Selected);
        byAlias.SelectedTemplate!.Template.Should().BeSameAs(engineCandidate);
        byEntry.SelectedTemplate!.Template.Should().BeSameAs(engineCandidate);
        byAlias.Candidates.Should().BeEmpty();
        byEntry.Candidates.Should().BeEmpty();

        templater.Dispose();

        Action accessDisposedCandidate = () => _ = byAlias.SelectedTemplate!.Template;
        accessDisposedCandidate.Should().Throw<ObjectDisposedException>();
        Func<Task> listDisposedSession = () => templater.ListAsync(TemplateType.Item);
        await listDisposedSession.Should().ThrowAsync<ObjectDisposedException>();
        Func<Task> resolveDisposedEntry = () => templater.ResolveAsync(entries.Single(), TemplateType.Item);
        await resolveDisposedEntry.Should().ThrowAsync<ObjectDisposedException>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }
}
