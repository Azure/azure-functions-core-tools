// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PackageIntake;

internal enum TemplatePackageFailure
{
    InvalidArchive,
    InvalidMetadata,
    IdentityMismatch,
    VersionMismatch,
    TypeMismatch,
    DigestMismatch,
    InvalidPath,
    PathCollision,
    UnsupportedEntry,
    IntegrityMismatch,
    ResourceLimitExceeded,
}

/// <summary>
/// Reports why the explicitly selected local template package was rejected.
/// </summary>
internal sealed class InvalidTemplatePackageException(TemplatePackageFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public TemplatePackageFailure Failure { get; } = failure;
}
