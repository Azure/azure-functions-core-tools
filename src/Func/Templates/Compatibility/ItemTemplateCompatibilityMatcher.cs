// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Admits items only from current, authoritative runtime facts and publisher-declared requirements.
/// </summary>
internal sealed class ItemTemplateCompatibilityMatcher
{
    public async Task<ItemTemplateAdmission> EvaluateAsync(
        FunctionsProject project,
        ItemTemplateRequirements requirements,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(requirements);
        cancellationToken.ThrowIfCancellationRequested();

        ProjectFactsContext context = CaptureContext(project, captureId);
        ProjectFacts? facts = await project.GetProjectFactsAsync(context, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ProjectFactsContext afterCapture = CaptureContext(project, captureId);
        List<ItemTemplateCompatibilityDiagnostic> diagnostics = [];
        if (!SameProject(context, afterCapture))
        {
            diagnostics.Add(new("project-changed", "The resolved project root or authority changed while facts were captured. Rerun admission."));
        }

        if (!requirements.IsDeclared)
        {
            diagnostics.Add(new("requirements-absent", "The item declares no project capability requirements. Compatibility cannot be established."));
        }

        if (context.Authority is null)
        {
            diagnostics.Add(new("project-authority-missing", "The resolved runtime does not provide an authority for project facts."));
        }

        if (facts is null)
        {
            diagnostics.Add(new("project-facts-missing", "The resolved runtime supplies no authoritative project facts."));
            return new(context, requirements, facts, ItemTemplateCompatibility.Unknown, diagnostics);
        }

        if (!string.Equals(context.ProjectRoot, facts.Context.ProjectRoot, StringComparison.Ordinal))
        {
            diagnostics.Add(new("project-root-mismatch", "The project facts belong to another project root. Recapture facts from the resolved project."));
        }

        if (!string.Equals(context.Authority, facts.Context.Authority, StringComparison.Ordinal))
        {
            diagnostics.Add(new("project-authority-mismatch", "The project facts do not belong to the resolved runtime authority."));
        }

        if (context.CaptureId != facts.Context.CaptureId)
        {
            diagnostics.Add(new("project-facts-stale", "The project facts belong to another operation. Recapture facts for this operation."));
        }

        if (!facts.IsAuthoritative)
        {
            diagnostics.Add(new("project-facts-not-authoritative", "The runtime could not establish authoritative project evidence."));
        }

        if (diagnostics.Count != 0)
        {
            return new(context, requirements, facts, ItemTemplateCompatibility.Unknown, diagnostics);
        }

        ILookup<string, ProjectCapability> capabilities = facts.Capabilities.ToLookup(capability => capability.Id, StringComparer.Ordinal);
        foreach (IGrouping<string, ProjectCapability> capability in capabilities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (capability.Select(fact => fact.Support).Distinct().Skip(1).Any())
            {
                diagnostics.Add(new("capability-conflicting",
                    $"The runtime reports conflicting project evidence for capability '{capability.Key}'.", capability.Key));
            }
        }

        if (diagnostics.Count != 0)
        {
            return new(context, requirements, facts, ItemTemplateCompatibility.Unknown, diagnostics);
        }

        bool incompatible = false;
        foreach (string id in requirements.CapabilityIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectCapability? capability = capabilities[id].FirstOrDefault();
            if (capability is null)
            {
                diagnostics.Add(new("capability-missing", $"The runtime provides no fact for required capability '{id}'.", id));
            }
            else if (capability.Support == ProjectCapabilitySupport.Unsupported)
            {
                incompatible = true;
                diagnostics.Add(new("capability-unsupported", $"The project does not support required capability '{id}'.", id));
            }
            else if (capability.Support != ProjectCapabilitySupport.Supported)
            {
                diagnostics.Add(new("capability-unknown", $"The runtime cannot establish required capability '{id}'.", id));
            }
        }

        ItemTemplateCompatibility compatibility = incompatible
            ? ItemTemplateCompatibility.Incompatible
            : diagnostics.Count == 0 ? ItemTemplateCompatibility.Compatible : ItemTemplateCompatibility.Unknown;
        return new(context, requirements, facts, compatibility, diagnostics);
    }

    /// <summary>
    /// Recaptures evidence for the same operation, invalidating the previous decision on any binding change.
    /// </summary>
    public async Task<ItemTemplateAdmission> RevalidateAsync(
        ItemTemplateAdmission admission,
        FunctionsProject project,
        ItemTemplateRequirements requirements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ItemTemplateAdmission current = await EvaluateAsync(project, requirements, admission.Context.CaptureId, cancellationToken);
        List<ItemTemplateCompatibilityDiagnostic> changes = [];
        if (!SameProject(admission.Context, current.Context))
        {
            changes.Add(new("project-changed", "The admission belongs to another project root or runtime authority. Make a new admission decision."));
        }

        if (admission.RequirementsFingerprint != current.RequirementsFingerprint)
        {
            changes.Add(new("requirements-changed", "The item requirements changed after admission. Make a new admission decision."));
        }

        if (admission.ProjectFactsFingerprint != current.ProjectFactsFingerprint)
        {
            changes.Add(new("project-facts-changed", "The project facts or inspected evidence changed after admission. Make a new admission decision."));
        }

        if (changes.Count != 0)
        {
            return new(current.Context, requirements, current.ProjectFacts, ItemTemplateCompatibility.Unknown, changes);
        }

        if (admission.Compatibility != current.Compatibility)
        {
            return new(current.Context, requirements, current.ProjectFacts, ItemTemplateCompatibility.Unknown,
                [new("admission-outcome-changed", "The admission outcome changed during evidence recapture. Make a new admission decision.")]);
        }

        return current;
    }

    private static ProjectFactsContext CaptureContext(FunctionsProject project, Guid captureId)
        => new(project.WorkingDirectory.Info.FullName, project.ProjectFactsAuthority, captureId);

    private static bool SameProject(ProjectFactsContext first, ProjectFactsContext second)
        => string.Equals(first.ProjectRoot, second.ProjectRoot, StringComparison.Ordinal)
            && string.Equals(first.Authority, second.Authority, StringComparison.Ordinal);
}
