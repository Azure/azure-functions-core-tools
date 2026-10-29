// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Preparation;

/// <summary>
/// Retains the selected package and template identifiers for diagnostics, not acquisition or execution authority.
/// </summary>
internal sealed class PreparedOperationIdentity(string packageId, string packageVersion, string templateIdentity)
{
    public string PackageId { get; } = Validate(packageId, nameof(packageId));

    public string PackageVersion { get; } = Validate(packageVersion, nameof(packageVersion));

    public string TemplateIdentity { get; } = Validate(templateIdentity, nameof(templateIdentity));

    private static string Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
