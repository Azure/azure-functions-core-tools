// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Verifies signed template archives in-process without requiring the .NET SDK.
/// </summary>
internal class TemplatePackageVerifier(TemplateNuGetSourceProvider sources)
{
    private readonly TemplateNuGetSourceProvider _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    /// <summary>
    /// Verifies signatures and configured trusted signers, accepting unsigned archives only under NuGet's accept policy.
    /// </summary>
    /// <exception cref="TemplatePackageException">The archive or its signature is invalid.</exception>
    public virtual async Task VerifyAsync(Stream archive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using PackageArchiveReader reader = new(archive, leaveStreamOpen: true);
            var policy = ClientPolicyContext.GetClientPolicy(_sources.GetSettings(), NullLogger.Instance);
            if (await reader.GetPrimarySignatureAsync(cancellationToken) is null)
            {
                if (policy.Policy == SignatureValidationMode.Require)
                {
                    throw new TemplatePackageException("The template package is unsigned, but NuGet.Config requires signing.");
                }

                return;
            }

            PackageSignatureVerifier identity = new([
                new IntegrityVerificationProvider(),
                new AllowListVerificationProvider(policy.AllowList, requireNonEmptyAllowList: policy.Policy == SignatureValidationMode.Require),
            ]);
            PackageSignatureVerifier trust = new([
                new SignatureTrustAndValidityVerificationProvider(policy.AllowList?
                    .Where(entry => entry.AllowUntrustedRoot)
                    .Select(entry => new KeyValuePair<string, NuGet.Common.HashAlgorithmName>(entry.Fingerprint, entry.FingerprintAlgorithm))),
            ]);

            // Integrity and configured trusted signers are always enforced. Chain trust follows restore's mode policy because
            // Linux and macOS stores lack code-signing roots outside the .NET SDK's bundle; require mode stays strict.
            await ThrowIfInvalidAsync(identity, reader, SignedPackageVerifierSettings.GetVerifyCommandDefaultPolicy(), cancellationToken);
            await ThrowIfInvalidAsync(trust, reader, policy.VerifierSettings, cancellationToken);
        }
        catch (Exception ex) when (ex is CryptographicException or PackagingException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new TemplatePackageException("The template archive is malformed or its signature is invalid.", ex);
        }
    }

    private static async Task ThrowIfInvalidAsync(
        PackageSignatureVerifier verifier, PackageArchiveReader reader, SignedPackageVerifierSettings settings, CancellationToken cancellationToken)
    {
        VerifySignaturesResult result = await verifier.VerifySignaturesAsync(reader, settings, cancellationToken);
        if (!result.IsValid)
        {
            string details = string.Join(" ", result.Results.SelectMany(r => r.GetErrorIssues()).Select(issue => issue.Message));
            throw new TemplatePackageException($"Template package signature verification failed. {details}");
        }
    }
}
