// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Functions.Cli.Workloads.Discovery;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using Rfc3161TimestampToken = System.Security.Cryptography.Pkcs.Rfc3161TimestampToken;

namespace Azure.Functions.Cli.Workloads.Install;

internal class WorkloadPackageTrust : IWorkloadPackageTrust
{
    private const string RootResource = "Azure.Functions.Cli.Workloads.Install.trusted-roots.pem";
    private const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";
    private const string TimestampingOid = "1.3.6.1.5.5.7.3.8";
    private const string LifetimeSigningOid = "1.3.6.1.4.1.311.10.3.13";

    protected virtual bool SupportsOfflineRevocation => !OperatingSystem.IsMacOS();

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
            if (!allowUntrusted)
            {
                await VerifyAsync(guard, cancellationToken);
            }

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

    internal async Task VerifyAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using PackageArchiveReader reader = new(stream, leaveStreamOpen: true);
            string id = reader.NuspecReader.GetId();
            if (!id.StartsWith("azure.functions.cli.workloads.", StringComparison.OrdinalIgnoreCase))
            {
                throw Reject("The package id is outside the official azure.functions.cli.workloads. namespace.");
            }

            PrimarySignature? signature = await reader.GetPrimarySignatureAsync(cancellationToken);
            if (signature is null || signature.Type != SignatureType.Author)
            {
                throw Reject("A primary NuGet author signature is required.");
            }

            // Use NuGet's archive-integrity verifier, but do not let its platform timestamp
            // trust policy replace the bundled anchors enforced below.
            SignedPackageVerifierSettings settings = new(
                allowUnsigned: false, allowIllegal: false, allowUntrusted: true,
                allowIgnoreTimestamp: false, allowMultipleTimestamps: false, allowNoTimestamp: true,
                allowUnknownRevocation: true, reportUnknownRevocation: false,
                verificationTarget: VerificationTarget.Author,
                signaturePlacement: SignaturePlacement.PrimarySignature,
                repositoryCountersignatureVerificationBehavior: SignatureVerificationBehavior.Never,
                revocationMode: RevocationMode.Offline);
            PackageSignatureVerifier verifier = new([new IntegrityVerificationProvider()]);
            VerifySignaturesResult result = await verifier.VerifySignaturesAsync(reader, settings, cancellationToken);
            if (!result.IsValid)
            {
                string details = string.Join(" ", result.Results.SelectMany(r => r.GetErrorIssues()).Select(issue => issue.Message));
                throw Reject($"The NuGet author signature, package integrity, or timestamp is invalid. {details}");
            }

            X509Certificate2Collection roots = LoadRoots();
            try
            {
                X509Certificate2 author = signature.SignerInfo.Certificate
                    ?? throw Reject("The author signing certificate is missing.");
                if (signature.Timestamps.Count > 1)
                {
                    throw Reject("Only one author signature timestamp is supported.");
                }

                Timestamp? timestamp = signature.Timestamps.SingleOrDefault();
                if (timestamp is not null)
                {
                    ValidateTimestamp(signature, timestamp, roots);
                }

                DateTime verificationTime = timestamp?.GeneralizedTime.UtcDateTime ?? DateTime.UtcNow;
                ValidateChain(author, signature.SignedCms.Certificates, roots, CodeSigningOid, verificationTime);
                if (!IsMicrosoftAuthor(author))
                {
                    throw Reject("The validated author certificate does not identify Microsoft Corporation.");
                }

                signature.SignerInfo.CheckSignature(verifySignatureOnly: true);
                if (!CertificateUtility.IsSignatureAlgorithmSupported(author)
                    || !CertificateUtility.IsCertificatePublicKeyValid(author)
                    || CertificateUtility.HasExtendedKeyUsage(author, LifetimeSigningOid)
                    || CertificateUtility.IsCertificateValidityPeriodInTheFuture(author))
                {
                    throw Reject("The NuGet author signature or certificate is invalid.");
                }

                // macOS treats Offline as Online. Keep its validation on the explicit NoCheck chains.
                if (SupportsOfflineRevocation)
                {
                    X509Certificate2Collection extraStore = [];
                    extraStore.AddRange(signature.SignedCms.Certificates);
                    extraStore.AddRange(roots);
                    SignatureVerifySettings signatureSettings = new(
                        allowIllegal: false, allowUntrusted: true, allowUnknownRevocation: true,
                        reportUnknownRevocation: false, reportUntrustedRoot: false, revocationMode: RevocationMode.Offline);
                    SignatureVerificationSummary summary = signature.Verify(timestamp!, signatureSettings,
                        NuGet.Common.HashAlgorithmName.SHA256, extraStore);
                    if (summary.Status != SignatureVerificationStatus.Valid)
                    {
                        throw Reject("The NuGet author signature or certificate is invalid.");
                    }
                }
            }
            finally
            {
                foreach (X509Certificate2 root in roots)
                {
                    root.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or PackagingException or InvalidDataException or System.Xml.XmlException)
        {
            throw new InvalidWorkloadException(
                "Workload publisher verification failed. Only explicitly trusted development packages should be installed with --allow-untrusted.", ex);
        }
    }

    private void ValidateTimestamp(PrimarySignature signature, Timestamp timestamp, X509Certificate2Collection roots)
    {
        byte[] encoded = timestamp.SignedCms.Encode();
        if (!Rfc3161TimestampToken.TryDecode(encoded, out Rfc3161TimestampToken? token, out int consumed)
            || consumed != encoded.Length
            || !SigningSpecifications.V1.AllowedHashAlgorithmOids.Contains(token.TokenInfo.HashAlgorithmId.Value)
            || !SigningSpecifications.V1.AllowedHashAlgorithmOids.Contains(timestamp.SignerInfo.DigestAlgorithm.Value)
            || !token.VerifySignatureForSignerInfo(signature.SignerInfo, out X509Certificate2? signer))
        {
            throw Reject("The author timestamp signature or message imprint is invalid.");
        }

        using RSA? key = signer.GetRSAPublicKey();
        if (key is null || key.KeySize < SigningSpecifications.V1.RSAPublicKeyMinLength
            || !SigningSpecifications.V1.AllowedSignatureAlgorithmOids.Contains(signer.SignatureAlgorithm.Value))
        {
            throw Reject("The timestamp certificate uses an unsupported signing algorithm or key.");
        }

        ValidateChain(signer, timestamp.SignedCms.Certificates, roots, TimestampingOid, timestamp.GeneralizedTime.UtcDateTime);
    }

    protected virtual X509Certificate2Collection LoadRoots()
    {
        using Stream stream = typeof(WorkloadPackageTrust).Assembly.GetManifestResourceStream(RootResource)
            ?? throw new InvalidOperationException("The bundled workload trust anchors are missing.");
        using StreamReader reader = new(stream);
        X509Certificate2Collection roots = [];
        roots.ImportFromPem(reader.ReadToEnd());
        return roots;
    }

    private static bool IsMicrosoftAuthor(X509Certificate2 certificate)
    {
        Dictionary<string, string> expected = new(StringComparer.Ordinal)
        {
            ["2.5.4.3"] = "Microsoft Corporation",
            ["2.5.4.10"] = "Microsoft Corporation",
            ["2.5.4.7"] = "Redmond",
            ["2.5.4.8"] = "Washington",
            ["2.5.4.6"] = "US",
        };
        foreach (X500RelativeDistinguishedName name in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            if (name.HasMultipleElements || !expected.Remove(name.GetSingleElementType().Value!, out string? value)
                || !string.Equals(name.GetSingleElementValue(), value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return expected.Count == 0;
    }

    private void ValidateChain(X509Certificate2 certificate, X509Certificate2Collection extraStore,
        X509Certificate2Collection roots, string applicationPolicy, DateTime verificationTime)
    {
        using X509Chain chain = new();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        chain.ChainPolicy.ExtraStore.AddRange(extraStore);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(applicationPolicy));
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationTime = verificationTime;
        if (!chain.Build(certificate))
        {
            throw Reject("The signing certificate does not have a valid chain to a bundled workload trust anchor.");
        }
    }

    private static InvalidWorkloadException Reject(string reason)
        => new($"Workload publisher verification failed: {reason} " +
            "Use --allow-untrusted only for explicitly trusted development packages.");
}
