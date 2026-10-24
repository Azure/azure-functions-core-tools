// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Records a compatibility decision and its immutable evidence.
/// It does not authorize template preparation, generation, execution, or publication.
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

    /// <summary>
    /// Gets whether this result passes the project-capability check.
    /// A true value is necessary, but not sufficient, for later preparation.
    /// </summary>
    public bool IsEligibleForPreparation => Compatibility == ItemTemplateCompatibility.Compatible;

    public IReadOnlyList<ItemTemplateCompatibilityDiagnostic> Diagnostics { get; } =
        Array.AsReadOnly<ItemTemplateCompatibilityDiagnostic>([.. diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))]);
}
