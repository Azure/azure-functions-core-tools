// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Compares item-template requirements with the resolved project's captured capabilities.
/// </summary>
internal sealed class ItemTemplateCompatibilityMatcher
{
    public ItemTemplateCompatibilityResult Evaluate(FunctionsProject project, ItemTemplateRequirements requirements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(requirements);
        cancellationToken.ThrowIfCancellationRequested();

        List<ItemTemplateCompatibilityDiagnostic> diagnostics = [];
        if (!requirements.IsDeclared)
        {
            diagnostics.Add(new("requirements-absent", "The item declares no project capability requirements. Compatibility requires a valid declaration."));
            return new(project, requirements, ItemTemplateCompatibility.Incompatible, diagnostics);
        }

        if (requirements.ValidationError is string validationError)
        {
            diagnostics.Add(new("requirements-invalid", validationError));
            return new(project, requirements, ItemTemplateCompatibility.Incompatible, diagnostics);
        }

        ILookup<string, ProjectCapability> capabilities = project.Capabilities.ToLookup(capability => capability.Id, StringComparer.Ordinal);
        foreach (IGrouping<string, ProjectCapability> capability in capabilities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectCapabilitySupport firstSupport = capability.First().Support;
            if (capability.Any(report => report.Support != firstSupport))
            {
                diagnostics.Add(new("capability-conflicting",
                    $"The language stack reports conflicting states for project capability '{capability.Key}'.", capability.Key));
            }
        }

        if (diagnostics.Count != 0)
        {
            return new(project, requirements, ItemTemplateCompatibility.Incompatible, diagnostics);
        }

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
                    diagnostics.Add(new("capability-unsupported", $"The project does not support required capability '{id}'.", id));
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected capability support value: {capability?.Support}.");
            }
        }

        ItemTemplateCompatibility compatibility = diagnostics.Count == 0 ? ItemTemplateCompatibility.Compatible : ItemTemplateCompatibility.Incompatible;
        return new(project, requirements, compatibility, diagnostics);
    }
}
