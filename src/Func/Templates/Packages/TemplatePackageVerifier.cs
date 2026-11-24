// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Verifies signed template archives in-process without requiring the .NET SDK.
/// </summary>
internal class TemplatePackageVerifier
{
    /// <summary>
    /// Verifies signed archive integrity and accepts unsigned archives without publisher or certificate-chain policy.
    /// </summary>
    /// <exception cref="TemplatePackageException">The archive or its signature is invalid.</exception>
    public virtual async Task VerifyAsync(Stream archive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using PackageArchiveReader reader = new(archive, leaveStreamOpen: true);
            if (await reader.GetPrimarySignatureAsync(cancellationToken) is null)
            {
                return;
            }

            PackageSignatureVerifier verifier = new([new IntegrityVerificationProvider()]);
            VerifySignaturesResult result = await verifier.VerifySignaturesAsync(
                reader, SignedPackageVerifierSettings.GetVerifyCommandDefaultPolicy(), cancellationToken);
            if (!result.IsValid)
            {
                string details = string.Join(" ", result.Results.SelectMany(r => r.GetErrorIssues()).Select(issue => issue.Message));
                throw new TemplatePackageException($"Template package integrity verification failed. {details}");
            }
        }
        catch (Exception ex) when (ex is CryptographicException or PackagingException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new TemplatePackageException("The template archive is malformed or its signature is invalid.", ex);
        }
    }
}
