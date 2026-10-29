// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Templates.Integration;

/// <summary>
/// Carries an exact package, caller selection policy, and publication target.
/// </summary>
internal sealed class PackageTemplateRequest(
    LocalTemplatePackageRequest package, TemplateSelectionRequest selection, string targetDirectory, string? name = null)
    : TemplatePreparationRequest(selection, targetDirectory, name)
{
    public LocalTemplatePackageRequest Package { get; } = package ?? throw new ArgumentNullException(nameof(package));

}
