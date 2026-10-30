// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using Azure.Functions.Cli.Workloads.Discovery;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;

namespace Azure.Functions.Cli.Workloads.Install;

internal class WorkloadPackageTrust : IWorkloadPackageTrust
{
    private static readonly IReadOnlyList<string> _trustedPublisherFingerprints =
    [
        "3F9001EA83C560D712C24CF213C3D312CB3BFF51EE89435D3430BD06B5D0EECE",
        "AA12DA22A49BCE7D5C1AE64CC1F3D892F150DA76140F210ABD2CBFFCA2C18A27",
        "566A31882BE208BE4422F7CFD66ED09F5D4524A5994F50CCC8B05EC0528C1353",
        "9A1B131BEE0605433056A4EA3815478A8E177961A968C6C0027C1093D1FEB630",
    ];

    private static readonly IReadOnlyCollection<VerificationAllowListEntry> _trustedPublishers =
        _trustedPublisherFingerprints
            .Select(fingerprint => new CertificateHashAllowListEntry(
                VerificationTarget.Author,
                SignaturePlacement.PrimarySignature,
                fingerprint,
                NuGet.Common.HashAlgorithmName.SHA256))
            .ToArray();

    internal static IReadOnlyList<string> PublisherFingerprints => _trustedPublisherFingerprints;

    public async Task<WorkloadPackageSnapshot> AcquireAsync(string path, bool allowUntrusted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.Combine(Path.GetTempPath(), $"func-workload-trust-{Guid.NewGuid():N}");
        string snapshotPath = Path.Combine(directory, "package.nupkg");
        FileStream? guard = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            await using (FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (FileStream destination = new(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            // Keep the private copy alive and deny write/delete sharing while all consumers reopen it.
            guard = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await VerifyAsync(guard, allowUntrusted, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            return new WorkloadPackageSnapshot(directory, snapshotPath, guard);
        }
        catch (Exception ex)
        {
            guard?.Dispose();
            new WorkloadPackageSnapshot(directory, snapshotPath, Stream.Null).Dispose();
            if (ex is (IOException and not FileNotFoundException) or UnauthorizedAccessException)
            {
                throw new InvalidWorkloadException($"Unable to read or stage workload archive '{path}': {ex.Message}", ex);
            }

            throw;
        }
    }

    internal async Task VerifyAsync(Stream stream, bool allowUntrusted, CancellationToken cancellationToken)
    {
        try
        {
            using PackageArchiveReader reader = new(stream, leaveStreamOpen: true);
            if (!allowUntrusted)
            {
                string id = reader.NuspecReader.GetId();
                if (!id.StartsWith("azure.functions.cli.workloads.", StringComparison.OrdinalIgnoreCase))
                {
                    throw Reject("The package id is outside the official azure.functions.cli.workloads. namespace.");
                }
            }

            PrimarySignature? signature;
            try
            {
                signature = await reader.GetPrimarySignatureAsync(cancellationToken);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw Reject("The NuGet author signature timestamp is invalid.", ex);
            }

            if (signature is null && allowUntrusted)
            {
                return;
            }

            if (!allowUntrusted && (signature is null || signature.Type != SignatureType.Author))
            {
                throw Reject("A primary NuGet author signature is required.");
            }

            PackageSignatureVerifier verifier = allowUntrusted
                ? new([new IntegrityVerificationProvider()])
                : CreateVerifier(signature!);
            VerifySignaturesResult result;
            try
            {
                SignedPackageVerifierSettings settings = allowUntrusted
                    ? CreateBypassVerifierSettings(signature!.Type == SignatureType.Author
                        ? VerificationTarget.Author
                        : VerificationTarget.Repository)
                    : CreateVerifierSettings();
                result = await verifier.VerifySignaturesAsync(reader, settings, cancellationToken);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw Reject("The NuGet author signature timestamp is invalid.", ex);
            }

            if (!result.IsValid)
            {
                string details = string.Join(" ", result.Results.SelectMany(r => r.GetErrorIssues()).Select(issue => issue.Message));
                throw Reject($"The NuGet author signature, package integrity, or timestamp is invalid. {details}");
            }
        }
        catch (Exception ex) when (ex is CryptographicException or PackagingException or InvalidDataException or System.Xml.XmlException)
        {
            string message = allowUntrusted
                ? "The workload archive is malformed or its signature integrity check failed."
                : "Workload publisher verification failed. Only explicitly trusted development packages should be installed with --allow-untrusted.";
            throw new InvalidWorkloadException(message, ex);
        }
    }

    protected virtual PackageSignatureVerifier CreateVerifier(PrimarySignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return new([
                new IntegrityVerificationProvider(),
                new SignatureTrustAndValidityVerificationProvider(),
                new AllowListVerificationProvider(_trustedPublishers, requireNonEmptyAllowList: true),
            ]);
    }

    internal virtual SignedPackageVerifierSettings CreateVerifierSettings()
        => new(
            allowUnsigned: false,
            allowIllegal: false,
            allowUntrusted: false,
            allowIgnoreTimestamp: false,
            allowMultipleTimestamps: false,
            allowNoTimestamp: true,
            allowUnknownRevocation: false,
            reportUnknownRevocation: true,
            verificationTarget: VerificationTarget.Author,
            signaturePlacement: SignaturePlacement.PrimarySignature,
            repositoryCountersignatureVerificationBehavior: SignatureVerificationBehavior.Never,
            revocationMode: RevocationMode.Online);

    private static SignedPackageVerifierSettings CreateBypassVerifierSettings(VerificationTarget verificationTarget)
        => new(
            allowUnsigned: true,
            allowIllegal: false,
            allowUntrusted: false,
            allowIgnoreTimestamp: true,
            allowMultipleTimestamps: true,
            allowNoTimestamp: true,
            allowUnknownRevocation: true,
            reportUnknownRevocation: false,
            verificationTarget: verificationTarget,
            signaturePlacement: SignaturePlacement.PrimarySignature,
            repositoryCountersignatureVerificationBehavior: SignatureVerificationBehavior.Never,
            revocationMode: RevocationMode.Offline);

    private static InvalidWorkloadException Reject(string reason)
        => new($"Workload publisher verification failed: {reason} " +
            "Use --allow-untrusted only for explicitly trusted development packages.");

    private static InvalidWorkloadException Reject(string reason, Exception innerException)
        => new($"Workload publisher verification failed: {reason} " +
            "Use --allow-untrusted only for explicitly trusted development packages.", innerException);
}
