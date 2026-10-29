// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PackageIntake;

/// <summary>
/// Identifies one explicit local archive and the package metadata and optional SHA-256 it must match.
/// </summary>
internal sealed class LocalTemplatePackageRequest(
    string archivePath, string expectedPackageId, string expectedVersion, string expectedPackageType, string? expectedSha256 = null)
    : TemplatePackageIdentity(expectedPackageId, expectedVersion, expectedPackageType, expectedSha256)
{
    public string ArchivePath { get; } = ValidatePath(archivePath);

    private static string ValidatePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(archivePath));
        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("Specify an absolute path to the local template archive.", nameof(archivePath));
        }

        return value;
    }

}
