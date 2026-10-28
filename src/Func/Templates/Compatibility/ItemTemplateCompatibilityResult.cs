// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Records a project compatibility result and the captured inputs it evaluated.
/// It does not authorize template preparation, generation, execution, or publication.
/// </summary>
internal sealed class ItemTemplateCompatibilityResult(
    FunctionsProject project,
    ItemTemplateRequirements requirements,
    ItemTemplateCompatibility compatibility,
    IEnumerable<ItemTemplateCompatibilityDiagnostic> diagnostics)
{
    public FunctionsProject Project { get; } = project ?? throw new ArgumentNullException(nameof(project));

    public ItemTemplateRequirements Requirements { get; } = requirements ?? throw new ArgumentNullException(nameof(requirements));

    public ItemTemplateCompatibility Compatibility { get; } = compatibility;

    /// <summary>
    /// Gets whether this result passes the project-capability check.
    /// A true value is necessary, but not sufficient, for later preparation.
    /// </summary>
    public bool PassesProjectCapabilityCheck => Compatibility == ItemTemplateCompatibility.Compatible;

    public IReadOnlyList<ItemTemplateCompatibilityDiagnostic> Diagnostics { get; } =
        Array.AsReadOnly<ItemTemplateCompatibilityDiagnostic>([.. diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))]);
}
