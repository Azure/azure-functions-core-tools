// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.PackageIntake;

/// <summary>
/// Defines the exact package metadata and optional digest an archive must match.
/// </summary>
internal class TemplatePackageIdentity(string expectedPackageId, string expectedVersion, string expectedPackageType, string? expectedSha256 = null)
{
    public string ExpectedPackageId { get; } = ValidateId(expectedPackageId);

    public string ExpectedVersion { get; } = ValidateVersion(expectedVersion);

    public string ExpectedPackageType { get; } = ValidateType(expectedPackageType);

    public string? ExpectedSha256 { get; } = ValidateDigest(expectedSha256);

    private static string ValidateId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(expectedPackageId));
        if (!PackageIdValidator.IsValidPackageId(value))
        {
            throw new ArgumentException("Specify a valid NuGet package ID.", nameof(expectedPackageId));
        }

        return value;
    }

    private static string ValidateVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(expectedVersion));
        if (!NuGetVersion.TryParse(value, out NuGetVersion? version)
            || !string.IsNullOrEmpty(version.Metadata)
            || !string.Equals(value, version.ToNormalizedString(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Specify an exact normalized NuGet version without a range or build metadata.", nameof(expectedVersion));
        }

        return value;
    }

    private static string ValidateType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(expectedPackageType));
        if (value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
        {
            throw new ArgumentException("Specify a package type name containing only ASCII letters, digits, '.', '-' or '_'.", nameof(expectedPackageType));
        }

        return value;
    }

    private static string? ValidateDigest(string? value)
    {
        if (value is not null && (value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character))))
        {
            throw new ArgumentException("Specify the archive SHA-256 as 64 hexadecimal characters.", nameof(expectedSha256));
        }

        return value?.ToLowerInvariant();
    }
}
