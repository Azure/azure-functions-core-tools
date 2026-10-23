// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Records compatibility with immutable evidence. It does not authorize or perform template execution.
/// </summary>
internal sealed class ItemTemplateAdmission(
    ProjectFactsContext context,
    ItemTemplateRequirements requirements,
    ProjectFacts? projectFacts,
    ItemTemplateCompatibility compatibility,
    IEnumerable<ItemTemplateCompatibilityDiagnostic> diagnostics)
{
    public ProjectFactsContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    public ItemTemplateRequirements Requirements { get; } = requirements ?? throw new ArgumentNullException(nameof(requirements));

    public ProjectFacts? ProjectFacts { get; } = projectFacts;

    public string RequirementsFingerprint => Requirements.Fingerprint;

    public string? ProjectFactsFingerprint => ProjectFacts?.Fingerprint;

    public ItemTemplateCompatibility Compatibility { get; } = compatibility;

    public bool IsEligibleForPreparation => Compatibility == ItemTemplateCompatibility.Compatible;

    public IReadOnlyList<ItemTemplateCompatibilityDiagnostic> Diagnostics { get; } =
        Array.AsReadOnly<ItemTemplateCompatibilityDiagnostic>([.. diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))]);
}
