// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.ExceptionServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Quickstart;

internal sealed record QuickstartInstalledQuery(WorkingDirectory Directory, NativeTemplateOwnerIdentity Owner,
    BundleChannel? Channel = null, string? Stack = null, string? Language = null);

internal enum QuickstartAvailability
{
    AvailableToInstall,
    Installed,
    UpdateAvailable,
}

internal sealed record QuickstartActionableEntry(QuickstartCatalogEntry Entry, string Stack,
    NativeTemplateOwnerIdentity Owner, QuickstartAvailability Availability);

internal sealed record QuickstartInstalledProjection(QuickstartCatalog Catalog,
    IReadOnlyList<InstalledTemplateArtifact> Installed, IReadOnlyList<QuickstartActionableEntry> ActionableEntries,
    int ExcludedCount);

/// <summary>
/// Projects only quickstarts with a currently qualified, owner-bound init action.
/// </summary>
internal sealed class QuickstartInstalledTemplates(ITemplaterFactory templates, IBoundInitScopeFactory scopes,
    QuickstartQualification qualification)
{
    private readonly ITemplaterFactory _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    private readonly IBoundInitScopeFactory _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    private readonly QuickstartQualification _qualification = qualification ?? throw new ArgumentNullException(nameof(qualification));

    public async Task<QuickstartInstalledProjection> ProjectAsync(QuickstartCatalog catalog, QuickstartInstalledQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Directory);
        ArgumentNullException.ThrowIfNull(query.Owner);
        using BoundInitScope scope = await _scopes.CreateAsync(cancellationToken);
        List<OwnedStackCandidate> candidates = [];
        if (query.Stack is not null)
        {
            IProjectStack? selected = scope.Catalog.FindStack(query.Stack);
            if (selected is null || !scope.StackOwners.TryGetValue(selected.Stack, out WorkloadEntry? owner)
                || NativeTemplateOwnerIdentity.FromEntry(owner) != query.Owner)
            {
                throw new WorkloadPlanChangedException();
            }
            candidates.Add(new(selected, owner));
        }
        else
        {
            foreach (IProjectStack stack in scope.Catalog.Stacks)
            {
                if (scope.StackOwners.TryGetValue(stack.Stack, out WorkloadEntry? owner)
                    && NativeTemplateOwnerIdentity.FromEntry(owner) == query.Owner)
                {
                    candidates.Add(new(stack, owner));
                }
            }
        }

        string? requestedLanguage = query.Language is null ? null : scope.Catalog.GetCanonicalLanguage(query.Language);
        if (query.Language is not null && requestedLanguage is null)
        {
            throw new WorkloadPlanChangedException();
        }
        if (requestedLanguage is not null)
        {
            candidates.RemoveAll(candidate => !candidate.Stack.SupportedLanguages.Contains(requestedLanguage, StringComparer.OrdinalIgnoreCase));
        }
        if (candidates.Count == 0)
        {
            throw new WorkloadPlanChangedException();
        }

        using Templater templater = await _templates.CreateProjectAsync(new(query.Directory) { RequireCleanStore = true },
            scope.Availability, scope.Support, cancellationToken);
        List<ProjectedStack> projectedStacks = [];
        foreach (OwnedStackCandidate candidate in candidates)
        {
            IReadOnlyList<InstalledTemplateArtifact> installed = await templater.ProjectArtifactsAsync(scope.Verifier,
                NativeTemplateOwnerIdentity.FromEntry(candidate.Owner), query.Channel, cancellationToken, scope.BundledVerifier);
            foreach (InstalledTemplateArtifact artifact in installed)
            {
                ThrowSystemicFailure(artifact.Failure);
            }
            projectedStacks.Add(new(candidate, installed));
        }

        List<QuickstartActionableEntry> actionable = [];
        int excluded = 0;
        foreach (QuickstartCatalogEntry entry in catalog.Entries)
        {
            bool hasAction = false;
            foreach (ProjectedStack projected in projectedStacks)
            {
                QualifiedQuickstart qualified;
                try
                {
                    QuickstartInstalledQuery candidateQuery = query with
                    {
                        Owner = NativeTemplateOwnerIdentity.FromEntry(projected.Candidate.Owner),
                        Stack = projected.Candidate.Stack.Stack,
                    };
                    qualified = await _qualification.ResolveAsync(entry, candidateQuery, projected.Candidate.Owner, cancellationToken);
                }
                catch (BasicAcquisitionException exception) when (QuickstartQualification.IsCandidateRefusal(exception.Failure))
                {
                    continue;
                }

                QuickstartPackageReference reference = entry.PackageReference
                    ?? throw new InvalidOperationException("Qualified quickstart is missing its exact package nomination.");
                BasicAcquisitionApproval approval = qualified.Acquisition.Approval
                    ?? throw new InvalidOperationException("Qualified quickstart is missing its acquisition approval.");
                BasicOwnedTemplateUpdate? update = approval.UpdateFor(reference.PackageId);
                if (update is not null && !MatchesNomination(update.After, reference))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                        "The nominated package no longer matches the qualified owned update.");
                }

                InstalledTemplateArtifact[] exactInstalled = [.. projected.Installed.Where(item =>
                    IsExactDuplicate(entry, item, query.Owner, query.Channel)
                    && SupportsTemplateConfiguration(item.Entry, scope.Catalog, projected.Candidate.Stack, requestedLanguage))];
                bool hasConflictingInstalled = projected.Installed.Any(item => MatchesInstalledCoordinate(item, reference));
                if (exactInstalled.Length == 0 && update is null && hasConflictingInstalled)
                {
                    continue;
                }

                QuickstartAvailability availability = update is not null
                    ? QuickstartAvailability.UpdateAvailable
                    : exactInstalled.Length > 0 ? QuickstartAvailability.Installed : QuickstartAvailability.AvailableToInstall;
                actionable.Add(new(entry, projected.Candidate.Stack.Stack,
                    NativeTemplateOwnerIdentity.FromEntry(projected.Candidate.Owner), availability));
                hasAction = true;
            }
            if (!hasAction)
            {
                excluded++;
            }
        }

        IReadOnlyList<InstalledTemplateArtifact> allInstalled = Array.AsReadOnly<InstalledTemplateArtifact>(
            [.. projectedStacks.SelectMany(projected => projected.Installed)]);
        QuickstartCatalog actionableCatalog = catalog with
        {
            Entries = actionable.Where(item => item.Availability != QuickstartAvailability.Installed)
                .Select(item => item.Entry).ToList().AsReadOnly(),
        };
        return new(actionableCatalog, allInstalled, actionable.AsReadOnly(), excluded);
    }

    internal static bool SupportsTemplateConfiguration(TemplateCatalogEntry entry, InstalledProjectStackCatalog catalog,
        IProjectStack selectedStack, string? requestedLanguage)
    {
        if (entry.MetadataDiagnostic is not null || entry.ProjectConfigurationDiagnostic is not null
            || entry.ConstraintEvaluation.Status != TemplateConstraintEvaluationStatus.Eligible
            || entry.ProjectConfigurations is not { Count: > 0 } declarations)
        {
            return false;
        }

        foreach (TemplateProjectConfigurationDeclaration declaration in declarations)
        {
            IProjectStack? declaredStack = catalog.FindStack(declaration.Stack);
            string? declaredLanguage = catalog.GetCanonicalLanguage(declaration.Language);
            if (declaredStack is null || declaredLanguage is null
                || !string.Equals(declaration.Stack, declaredStack.Stack, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(declaredStack.Stack, selectedStack.Stack, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(declaration.Language, declaredLanguage, StringComparison.OrdinalIgnoreCase)
                || !declaredStack.SupportedLanguages.Contains(declaredLanguage, StringComparer.OrdinalIgnoreCase)
                || requestedLanguage is not null && !string.Equals(declaredLanguage, requestedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsExactDuplicate(QuickstartCatalogEntry entry, InstalledTemplateArtifact installed,
        NativeTemplateOwnerIdentity owner, BundleChannel? channel)
    {
        if (installed.Diagnostic is not null || installed.Failure is not null || entry.MetadataStatus != QuickstartPackageMetadataStatus.Unverified
            || entry.PackageReference is not { Artifact: { } digest } reference
            || !string.Equals(digest.Algorithm, "sha256", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (installed.Selection is { } external)
        {
            return external.Owner == owner && external.Channel == channel
                && MatchesNomination(external.TemplateIdentity, external.Artifact.PackageId, external.Artifact.Version,
                    external.Source, external.Artifact.Sha256, reference);
        }
        if (installed.BundledSelection is { } bundled)
        {
            return channel is null && bundled.Owner == owner
                && MatchesNomination(bundled.TemplateIdentity, bundled.Artifact.PackageId, bundled.Artifact.Version,
                    bundled.Declaration.Source, bundled.Artifact.Sha256, reference);
        }
        return false;
    }

    internal static bool MatchesInstalledCoordinate(InstalledTemplateArtifact installed, QuickstartPackageReference reference)
    {
        TemplatePackageInfo? package = installed.Entry.Package;
        return string.Equals(installed.Entry.Identity, reference.TemplateIdentity, StringComparison.Ordinal)
            && string.Equals(package?.Identifier, reference.PackageId, StringComparison.OrdinalIgnoreCase)
            && NuGetVersion.TryParse(package?.Version, out NuGetVersion? version)
            && NuGetVersion.TryParse(reference.Version, out NuGetVersion? nominated)
            && version == nominated;
    }

    private static bool MatchesNomination(BasicTemplateArtifact artifact, QuickstartPackageReference reference)
        => MatchesNomination(reference.TemplateIdentity, artifact.PackageId, artifact.Version.ToNormalizedString(),
            artifact.Source, artifact.Sha256, reference);

    private static bool MatchesNomination(string identity, string packageId, string version, string source, string sha256,
        QuickstartPackageReference reference)
        => string.Equals(identity, reference.TemplateIdentity, StringComparison.Ordinal)
            && string.Equals(packageId, reference.PackageId, StringComparison.OrdinalIgnoreCase)
            && NuGetVersion.TryParse(version, out NuGetVersion? actual)
            && NuGetVersion.TryParse(reference.Version, out NuGetVersion? nominated)
            && actual == nominated && string.Equals(source, reference.Source, StringComparison.Ordinal)
            && string.Equals(sha256, reference.Artifact?.Digest, StringComparison.OrdinalIgnoreCase);

    internal static void ThrowSystemicFailure(Exception? failure)
    {
        if (failure is null || failure is InvalidTemplateMetadataException
            || failure is BasicAcquisitionException acquisitionFailure
                && QuickstartQualification.IsCandidateRefusal(acquisitionFailure.Failure))
        {
            return;
        }
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed record OwnedStackCandidate(IProjectStack Stack, WorkloadEntry Owner);

    private sealed record ProjectedStack(OwnedStackCandidate Candidate, IReadOnlyList<InstalledTemplateArtifact> Installed);
}
