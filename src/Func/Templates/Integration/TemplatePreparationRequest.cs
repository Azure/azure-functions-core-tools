// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Templates.Integration;

/// <summary>
/// Binds template selection and naming to one publication target.
/// </summary>
internal class TemplatePreparationRequest(TemplateSelectionRequest selection, string targetDirectory, string? name = null)
{
    public TemplateSelectionRequest Selection { get; } = selection ?? throw new ArgumentNullException(nameof(selection));

    public string TargetDirectory { get; } = PreparedOperationPlan.NormalizeTargetDirectory(targetDirectory);

    public string? Name { get; } = name;

    public string? RequiredProjectDefault { get; init; }
}
