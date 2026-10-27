// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Evaluates compatibility using current, authoritative stack-reported capabilities and publisher-declared requirements.
/// </summary>
internal sealed class ItemTemplateCompatibilityMatcher
{
    public async Task<ItemTemplateCompatibilityResult> EvaluateAsync(
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
            diagnostics.Add(new("project-changed", "The resolved project root or stack implementation changed while project facts were captured. Start a new compatibility evaluation."));
        }

        if (!requirements.IsDeclared)
        {
            diagnostics.Add(new("requirements-absent", "The item declares no project capability requirements. Compatibility cannot be established."));
        }

        if (context.Authority is null)
        {
            diagnostics.Add(new("project-authority-missing", "The resolved project does not provide a canonical stack identifier for project facts."));
        }

        if (facts is null)
        {
            diagnostics.Add(new("project-facts-missing", "The resolved stack supplies no authoritative project facts."));
            return new(context, requirements, facts, ItemTemplateCompatibility.Unknown, diagnostics);
        }

        if (!string.Equals(context.ProjectRoot, facts.Context.ProjectRoot, StringComparison.Ordinal))
        {
            diagnostics.Add(new("project-root-mismatch", "The project facts belong to another project root. Recapture facts from the resolved project."));
        }

        if (!string.Equals(context.Authority, facts.Context.Authority, StringComparison.Ordinal))
        {
            diagnostics.Add(new("project-authority-mismatch", "The project facts do not belong to the resolved stack authority."));
        }

        if (context.CaptureId != facts.Context.CaptureId)
        {
            diagnostics.Add(new("project-facts-stale", "The project facts belong to another operation. Recapture facts for this operation."));
        }

        if (!facts.IsAuthoritative)
        {
            diagnostics.Add(new("project-facts-not-authoritative", "The stack implementation could not establish authoritative project evidence."));
        }

        if (diagnostics.Count != 0)
        {
            return new(context, requirements, facts, ItemTemplateCompatibility.Unknown, diagnostics);
        }

        ILookup<string, ProjectCapability> capabilities = facts.Capabilities.ToLookup(capability => capability.Id, StringComparer.Ordinal);
        foreach (IGrouping<string, ProjectCapability> capability in capabilities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectCapabilitySupport firstSupport = capability.First().Support;
            if (capability.Any(report => report.Support != firstSupport))
            {
                diagnostics.Add(new("capability-conflicting",
                    $"The stack reports conflicting project evidence for capability '{capability.Key}'.", capability.Key));
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
            switch (capability?.Support)
            {
                case null:
                    diagnostics.Add(new("capability-missing", $"The stack provides no report for required capability '{id}'.", id));
                    break;
                case ProjectCapabilitySupport.Supported:
                    break;
                case ProjectCapabilitySupport.Unsupported:
                    incompatible = true;
                    diagnostics.Add(new("capability-unsupported", $"The project does not support required capability '{id}'.", id));
                    break;
                case ProjectCapabilitySupport.Unknown:
                    diagnostics.Add(new("capability-unknown", $"The stack cannot establish required capability '{id}'.", id));
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected capability support value: {capability?.Support}.");
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
    public async Task<ItemTemplateCompatibilityResult> RevalidateAsync(
        ItemTemplateCompatibilityResult previousResult,
        FunctionsProject project,
        ItemTemplateRequirements requirements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previousResult);
        ItemTemplateCompatibilityResult current = await EvaluateAsync(project, requirements, previousResult.Context.CaptureId, cancellationToken);
        List<ItemTemplateCompatibilityDiagnostic> changes = [];
        if (!SameProject(previousResult.Context, current.Context))
        {
            changes.Add(new("project-changed", "The recorded compatibility decision belongs to a different project root or stack implementation. Start a new compatibility evaluation."));
        }

        if (previousResult.RequirementsFingerprint != current.RequirementsFingerprint)
        {
            changes.Add(new("requirements-changed", "The template's capability requirements changed after compatibility was evaluated. Start a new compatibility evaluation."));
        }

        if (previousResult.ProjectFactsFingerprint != current.ProjectFactsFingerprint)
        {
            changes.Add(new("project-facts-changed", "The project facts or inspected inputs changed after compatibility was evaluated. Start a new compatibility evaluation."));
        }

        if (changes.Count != 0)
        {
            return new(current.Context, requirements, current.ProjectFacts, ItemTemplateCompatibility.Unknown, changes);
        }

        if (previousResult.Compatibility != current.Compatibility)
        {
            return new(current.Context, requirements, current.ProjectFacts, ItemTemplateCompatibility.Unknown,
                [new("admission-outcome-changed", "The compatibility result changed when project facts were captured again. Start a new compatibility evaluation.")]);
        }

        return current;
    }

    private static ProjectFactsContext CaptureContext(FunctionsProject project, Guid captureId)
        => new(project.WorkingDirectory.Info.FullName, project.StackName, captureId);

    private static bool SameProject(ProjectFactsContext first, ProjectFactsContext second)
        => string.Equals(first.ProjectRoot, second.ProjectRoot, StringComparison.Ordinal)
            && string.Equals(first.Authority, second.Authority, StringComparison.Ordinal);
}
