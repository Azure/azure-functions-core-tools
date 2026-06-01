// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography.X509Certificates;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace Azure.Functions.Cli.Workloads.Install.Trust;

/// <summary>
/// Default <see cref="IWorkloadTrustVerifier"/>. Enforces two gates:
/// (a) the package id must live under <see cref="OfficialPackageIdPrefix"/>,
/// and (b) the <c>.nupkg</c> must carry a primary signature whose chain
/// terminates at a trust anchor in the bundled
/// <see cref="ITrustAnchorStore"/>. <c>--allow-untrusted</c> skips both
/// gates so unsigned local packs can install during development.
/// </summary>
internal sealed class WorkloadTrustVerifier(ITrustAnchorStore anchorStore) : IWorkloadTrustVerifier
{
    /// <summary>
    /// Package id prefix that identifies a workload as part of the official
    /// Microsoft-published Azure Functions CLI surface. Case-insensitive.
    /// </summary>
    public const string OfficialPackageIdPrefix = "azure.functions.cli.workloads.";

    private readonly ITrustAnchorStore _anchorStore = anchorStore ?? throw new ArgumentNullException(nameof(anchorStore));

    /// <inheritdoc />
    public async Task<TrustVerificationResult> VerifyAsync(
        string nupkgPath,
        string packageId,
        bool allowUntrusted,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        if (allowUntrusted)
        {
            return TrustVerificationResult.Trusted;
        }

        if (!packageId.StartsWith(OfficialPackageIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TrustVerificationResult.Untrusted(
                $"Package '{packageId}' is not part of the Azure Functions CLI workload namespace " +
                $"(expected id prefix '{OfficialPackageIdPrefix}'). " +
                $"Pass --allow-untrusted to install anyway.");
        }

        X509Certificate2Collection anchors = _anchorStore.GetTrustAnchors();
        if (anchors.Count == 0)
        {
            return TrustVerificationResult.Untrusted(
                "No trust anchors are configured in the embedded trust bundle. " +
                "Reinstall the CLI or pass --allow-untrusted to override.");
        }

        return await VerifySignatureAsync(nupkgPath, packageId, anchors, cancellationToken);
    }

    private static async Task<TrustVerificationResult> VerifySignatureAsync(
        string nupkgPath,
        string packageId,
        X509Certificate2Collection anchors,
        CancellationToken cancellationToken)
    {
        using var reader = new PackageArchiveReader(File.OpenRead(nupkgPath));

        bool isSigned;
        try
        {
            isSigned = await reader.IsSignedAsync(cancellationToken);
        }
        catch (SignatureException ex)
        {
            return TrustVerificationResult.Untrusted(
                $"Package '{packageId}' has a malformed signature: {ex.Message}. " +
                "Pass --allow-untrusted to install anyway.");
        }

        if (!isSigned)
        {
            return TrustVerificationResult.Untrusted(
                $"Package '{packageId}' is not signed. " +
                "Pass --allow-untrusted to install anyway.");
        }

        PrimarySignature signature;
        try
        {
            signature = await reader.GetPrimarySignatureAsync(cancellationToken);
        }
        catch (SignatureException ex)
        {
            return TrustVerificationResult.Untrusted(
                $"Package '{packageId}' signature could not be read: {ex.Message}. " +
                "Pass --allow-untrusted to install anyway.");
        }

        // Build an X.509 chain using ONLY our embedded anchors. CustomRootTrust
        // bypasses the OS trust store entirely so we don't end up trusting a
        // package signed by every public CA the machine happens to trust.
        X509Certificate2? signer = signature.SignerInfo.Certificate;
        if (signer is null)
        {
            return TrustVerificationResult.Untrusted(
                $"Package '{packageId}' is signed but the signer certificate could not be extracted. " +
                "Pass --allow-untrusted to install anyway.");
        }

        // Feed any intermediates that travelled with the signature into the
        // chain builder so it can stitch leaf-to-anchor without needing AIA
        // network fetches (and without trusting the intermediates as roots).
        var extraStore = new X509Certificate2Collection();
        foreach (X509Certificate2 cert in signature.SignedCms.Certificates)
        {
            extraStore.Add(cert);
        }

        return IsChainTrustedTo(signer, anchors, extraStore, out string failure)
            ? TrustVerificationResult.Trusted
            : TrustVerificationResult.Untrusted(
                $"Package '{packageId}' signature does not chain to a trusted publisher: {failure}. " +
                "Pass --allow-untrusted to install anyway.");
    }

    /// <summary>
    /// Builds an X.509 chain for <paramref name="signer"/> using
    /// <paramref name="anchors"/> as the only acceptable roots and
    /// <paramref name="extraStore"/> as candidate intermediates. Returns
    /// <c>true</c> when the chain validates; <paramref name="failure"/>
    /// is a human-readable summary of the chain status on failure.
    /// </summary>
    /// <remarks>
    /// Revocation is intentionally disabled: enabling OCSP/CRL on a CLI
    /// install path means every install needs network egress to a CA's
    /// responder, which is a hostile dependency for offline / restricted
    /// environments. Revocation belongs behind an opt-in flag in a
    /// follow-up.
    /// </remarks>
    internal static bool IsChainTrustedTo(
        X509Certificate2 signer,
        X509Certificate2Collection anchors,
        X509Certificate2Collection extraStore,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(extraStore);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.AddRange(anchors);
        chain.ChainPolicy.ExtraStore.AddRange(extraStore);

        if (chain.Build(signer))
        {
            failure = string.Empty;
            return true;
        }

        failure = chain.ChainStatus.Length == 0
            ? "chain could not be built"
            : string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
        return false;
    }
}
