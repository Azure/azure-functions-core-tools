// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NSubstitute;
using Xunit.Abstractions;
using HashAlgorithmName = System.Security.Cryptography.HashAlgorithmName;
using Rfc3161TimestampTokenInfo = System.Security.Cryptography.Pkcs.Rfc3161TimestampTokenInfo;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed partial class WorkloadPackageTrustTests(ITestOutputHelper output) : WorkloadInstallerTestBase
{
    private const string MicrosoftSubject = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
    private const string OfficialId = "Azure.Functions.Cli.Workloads.Test";

    [Theory]
    [InlineData("Test.Workload", "*namespace*")]
    [InlineData(OfficialId, "*author signature*")]
    public async Task StrictInstall_RejectsBeforeMetadataExtraction(string id, string message)
    {
        string package = BuildNupkg(id: id);
        MetadataReader.ClearReceivedCalls();
        WorkloadInstaller installer = NewInstaller(packageTrust: new WorkloadPackageTrust());

        Func<Task> act = () => installer.InstallFromPackageAsync(package);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage(message);
        MetadataReader.ReceivedCalls().Should().BeEmpty();
        Store.ReceivedCalls().Should().BeEmpty();
        File.Exists(package).Should().BeTrue();
    }

    [Fact]
    public async Task Bypass_AllowsUnsignedUnofficialPackageButStillValidatesFormat()
    {
        WorkloadInstaller installer = NewInstaller(packageTrust: new WorkloadPackageTrust());
        string valid = BuildNupkg();
        string invalid = BuildNupkg(includeFuncCliWorkloadType: false);

        var result = await installer.InstallFromPackageAsync(valid, allowUntrusted: true);
        result.Entry.PackageId.Should().Be("test.workload");
        Func<Task> act = () => installer.InstallFromPackageAsync(invalid, allowUntrusted: true);
        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
        File.Exists(valid).Should().BeTrue();
        File.Exists(invalid).Should().BeTrue();
    }

    [Fact]
    public async Task Snapshot_IsIndependentOfUserSourceAndDeletedOnDispose()
    {
        string source = BuildNupkg();
        byte[] original = await File.ReadAllBytesAsync(source);
        WorkloadPackageTrust trust = new();
        string snapshotPath;
        using (WorkloadPackageSnapshot snapshot = await trust.AcquireAsync(source, true, TestContextToken()))
        {
            snapshotPath = snapshot.Path;
            await File.WriteAllTextAsync(source, "source replaced after acquisition");
            (await File.ReadAllBytesAsync(snapshotPath)).Should().Equal(original);
            if (OperatingSystem.IsWindows())
            {
                Action write = () => File.WriteAllText(snapshotPath, "changed");
                write.Should().Throw<IOException>();
                Action delete = () => File.Delete(snapshotPath);
                delete.Should().Throw<IOException>();
            }
            else
            {
                File.GetUnixFileMode(Path.GetDirectoryName(snapshotPath)!)
                    .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        File.Exists(snapshotPath).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(snapshotPath)).Should().BeFalse();
        File.ReadAllText(source).Should().Be("source replaced after acquisition");
    }

    [Fact]
    public async Task Cancellation_IsNotTranslatedToTrustFailure()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        string source = BuildNupkg();
        Func<Task> act = async () =>
        {
            using var snapshot = await new WorkloadPackageTrust().AcquireAsync(source, false, cancellation.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(source).Should().BeTrue();
    }

    [Theory]
    [InlineData(MicrosoftSubject)]
    [InlineData("C=US, S=Washington, L=Redmond, O=Microsoft Corporation, CN=Microsoft Corporation")]
    public async Task NuGetVerifier_AcceptsValidAuthorCertificates(string subject)
    {
        using X509Certificate2 root = CreateRoot();
        TestRootTrust trust = new(root);
        for (int index = 0; index < 2; index++)
        {
            using X509Certificate2 author = CreateAuthor(root, subject);
            string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
            using var snapshot = await trust.AcquireAsync(signed, false, TestContextToken());
            File.Exists(snapshot.Path).Should().BeTrue();
        }
    }

    [Fact]
    public async Task AuthorCertificateNotOnAllowlist_IsRejected()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
        TestRootTrust trust = new(root, trustedPublisherFingerprint: new string('0', 64));
        Func<Task> act = async () =>
        {
            using var snapshot = await trust.AcquireAsync(signed, false, TestContextToken());
        };

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
    }

    [Fact]
    public async Task MicrosoftSubject_UnderUnapprovedRoot_IsRejected()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
        Func<Task> act = async () =>
        {
            using var snapshot = await new WorkloadPackageTrust().AcquireAsync(signed, false, TestContextToken());
        };

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*not trusted*");
    }

    [Fact]
    public async Task SignedPackage_WithTamperedPayload_IsRejected()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
        using (ZipArchive archive = ZipFile.Open(signed, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.Entries.First(e => e.FullName.EndsWith("Test.dll", StringComparison.Ordinal));
            using Stream stream = entry.Open();
            stream.Write([1, 2, 3, 4]);
        }

        Func<Task> act = async () =>
        {
            using var snapshot = await new TestRootTrust(root).AcquireAsync(signed, false, TestContextToken());
        };
        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*integrity*");
    }

    [Fact]
    public async Task Bypass_StillRejectsTamperedSignedPackage()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
        using (ZipArchive archive = ZipFile.Open(signed, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.Entries.First(e => e.FullName.EndsWith("Test.dll", StringComparison.Ordinal));
            using Stream stream = entry.Open();
            stream.Write([1, 2, 3, 4]);
        }

        Func<Task> act = async () =>
        {
            using var snapshot = await new WorkloadPackageTrust().AcquireAsync(signed, true, TestContextToken());
        };

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*integrity*");
    }

    [Fact]
    public async Task Timestamp_FromUntrustedAuthority_IsRejected()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject, expired: true);
        using X509Certificate2 timestamper = CreateAuthor(root, "CN=Test Timestamp Authority", timestamping: true);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author, timestamper, root);
        Func<Task> act = async () =>
        {
            using var snapshot = await new TestRootTrust(root).AcquireAsync(signed, false, TestContextToken());
        };

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*timestamping certificate is not trusted*");
    }

    [Theory]
    [InlineData("local", false, false)]
    [InlineData("catalog", false, false)]
    [InlineData("update", false, false)]
    [InlineData("local", true, false)]
    [InlineData("catalog", true, false)]
    [InlineData("update", true, false)]
    [InlineData("local", true, true)]
    [InlineData("catalog", true, true)]
    [InlineData("update", true, true)]
    public async Task AcquisitionPaths_RejectUnsignedArchiveBeforeDeployment(string route, bool pointer, bool signedPointer)
    {
        const string id = "azure.functions.cli.workloads.test";
        const string implementationId = id + ".win-x64";
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string package = pointer
            ? BuildPointerNupkg(id, "1.1.0", $$"""{"win-x64":"{{implementationId}}"}""")
            : BuildNupkg(id: id, version: "1.1.0");
        string implementation = BuildRidImplementationNupkg(implementationId, "1.1.0", "win-x64");
        if (signedPointer)
        {
            package = await SignAsync(package, root, author);
        }

        var resolved = NewResolved(id, "1.1.0");
        var implementationResolved = NewResolved(implementationId, "1.1.0");
        Catalog.ResolveLatestVersionAsync(id, Arg.Any<bool?>(), Arg.Any<NuGetVersion?>(), Arg.Any<bool>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(resolved);
        Catalog.ResolveVersionAsync(implementationId, Arg.Any<NuGetVersion>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(implementationResolved);
        Catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>()).Returns(_ => File.OpenRead(package));
        Catalog.DownloadAsync(implementationResolved, Arg.Any<CancellationToken>()).Returns(_ => File.OpenRead(implementation));

        WorkloadEntry current = ExistingEntry(pointer ? implementationId : id, "1.0.0");
        if (pointer)
        {
            current = new WorkloadEntry
            {
                PackageId = implementationId,
                PackageVersion = "1.0.0",
                InstallRefCount = 1,
                LogicalPackage = new LogicalPackage { PackageId = id, PackageVersion = "1.0.0", Source = resolved.Source.Source },
                RuntimeIdentifier = "win-x64",
            };
        }

        if (route == "update")
        {
            Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);
            string oldDirectory = Paths.GetInstallDirectory(current.PackageId, current.PackageVersion);
            Directory.CreateDirectory(oldDirectory);
            File.WriteAllText(Path.Combine(oldDirectory, "existing.txt"), "keep installed version");
        }

        Store.ClearReceivedCalls();
        WorkloadInstaller installer = NewInstaller(metadataReader: pointer ? new WorkloadMetadataReader() : null,
            packageTrust: new TestRootTrust(root));
        Func<Task> act = route switch
        {
            "local" => () => installer.InstallFromPackageAsync(package),
            "catalog" => () => installer.InstallFromCatalogAsync(id, null, null, false, true, false),
            _ => () => installer.UpdateAsync(id, null, null, false, false,
                pointer ? WorkloadOwnershipKind.Logical : WorkloadOwnershipKind.Explicit),
        };

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*primary NuGet author signature*");
        await Store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
        await Store.DidNotReceive().MoveExplicitOwnershipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkloadEntry>(),
            Arg.Any<CancellationToken>());
        await Store.DidNotReceive().MoveLogicalOwnershipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkloadEntry>(),
            Arg.Any<CancellationToken>());
        Directory.Exists(Paths.GetInstallDirectory(current.PackageId, "1.1.0")).Should().BeFalse();
        if (route == "update")
        {
            File.ReadAllText(Path.Combine(Paths.GetInstallDirectory(current.PackageId, "1.0.0"), "existing.txt"))
                .Should().Be("keep installed version");
        }
        File.Exists(package).Should().BeTrue();
        File.Exists(implementation).Should().BeTrue();
    }

    [Fact]
    public async Task Deployment_UsesVerifiedSnapshotEvenWhenOriginalIsReplaced()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
        WorkloadInstaller installer = NewInstaller(packageTrust: new TestRootTrust(root,
            () => File.WriteAllText(signed, "replaced after snapshot was created")));

        var result = await installer.InstallFromPackageAsync(signed);

        string payload = Path.Combine(Paths.GetInstallDirectory(result.Entry.PackageId, result.Entry.PackageVersion), "tools", "any", "Test.dll");
        File.ReadAllBytes(payload).Should().Equal([0x4D, 0x5A]);
        File.ReadAllText(signed).Should().Be("replaced after snapshot was created");
    }

    [Fact]
    public async Task SuccessfulVerificationWhichCancelsToken_DoesNotDeploy()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
        using CancellationTokenSource cancellation = new();
        WorkloadInstaller installer = NewInstaller(packageTrust: new TestRootTrust(root, cancellation.Cancel));
        MetadataReader.ClearReceivedCalls();

        Func<Task> act = () => installer.InstallFromPackageAsync(signed, cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        MetadataReader.ReceivedCalls().Should().BeEmpty();
        Store.ReceivedCalls().Should().BeEmpty();
        File.Exists(signed).Should().BeTrue();
    }

    [Fact]
    public async Task CatalogReuse_DoesNotRetroactivelyVerifyHistoricalInstall()
    {
        var current = ExistingEntry("legacy.unsigned.workload", "1.0.0");
        Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);
        Directory.CreateDirectory(Paths.GetInstallDirectory(current.PackageId, current.PackageVersion));
        var resolved = NewResolved(current.PackageId, current.PackageVersion);
        Catalog.ResolveLatestVersionAsync(current.PackageId, Arg.Any<bool?>(), null, true, null,
            Arg.Any<CancellationToken>()).Returns(resolved);
        WorkloadInstaller installer = NewInstaller(packageTrust: new WorkloadPackageTrust());

        var result = await installer.InstallFromCatalogAsync(current.PackageId, null, null, false, true, false);

        result.AlreadyInstalled.Should().BeTrue();
        result.Entry.Should().BeSameAs(current);
        await Catalog.DidNotReceive().DownloadAsync(Arg.Any<Azure.Functions.Cli.Workloads.Catalog.ResolvedPackage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepositoryPrimarySignature_DoesNotQualifyAsAuthorSignature()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author, repository: true);
        using (PackageArchiveReader reader = new(signed))
        {
            (await reader.GetPrimarySignatureAsync(CancellationToken.None)).Type.Should().Be(SignatureType.Repository);
        }

        Func<Task> act = async () =>
        {
            using var snapshot = await new TestRootTrust(root).AcquireAsync(signed, false, CancellationToken.None);
        };
        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*primary NuGet author signature*");

        using WorkloadPackageSnapshot bypassed = await new WorkloadPackageTrust().AcquireAsync(
            signed, allowUntrusted: true, cancellationToken: CancellationToken.None);
        File.Exists(bypassed.Path).Should().BeTrue();
    }

    [Fact]
    public async Task CorruptSignature_WithValidAuthorCertificate_IsRejected()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author, corruptSignature: true);
        Func<Task> act = async () =>
        {
            using var snapshot = await new TestRootTrust(root).AcquireAsync(signed, false, CancellationToken.None);
        };
        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
    }

    [Fact]
    public void PublisherFingerprintAllowlist_ContainsVerifiedMicrosoftSigners()
    {
        WorkloadPackageTrust.PublisherFingerprints.Should().Contain(
            "9A1B131BEE0605433056A4EA3815478A8E177961A968C6C0027C1093D1FEB630");
        WorkloadPackageTrust.PublisherFingerprints.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void VerificationSettings_RequireOnlineCertificateStatus()
    {
        SignedPackageVerifierSettings settings = new WorkloadPackageTrust().CreateVerifierSettings();

        settings.AllowUntrusted.Should().BeFalse();
        settings.AllowUnknownRevocation.Should().BeFalse();
        settings.AllowUnsigned.Should().BeFalse();
        settings.AllowMultipleTimestamps.Should().BeFalse();
        settings.AllowNoTimestamp.Should().BeTrue();
        settings.RevocationMode.Should().Be(NuGet.Common.RevocationMode.Online);
    }

    [Fact]
    public async Task AccessDenied_IsTranslatedWithoutSuggestingTrustBypass()
    {
        Func<Task> act = async () =>
        {
            using var snapshot = await new WorkloadPackageTrust().AcquireAsync(Root, false, CancellationToken.None);
        };

        var exception = await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*Unable to read or stage*");
        exception.Which.InnerException.Should().BeOfType<UnauthorizedAccessException>();
        exception.Which.Message.Should().NotContain("--allow-untrusted");
    }

    private static CancellationToken TestContextToken() => CancellationToken.None;

    private static X509Certificate2 CreateRoot()
    {
        using var key = RSA.Create(2048);
        CertificateRequest request = new("CN=Workload Tests Root", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(3));
    }

    private static X509Certificate2 CreateAuthor(X509Certificate2 root, string subject, bool expired = false, bool timestamping = false,
        int keySize = 2048, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null, bool lifetimeSigning = false,
        string? revocationUrl = null, bool weakCertificateSignature = false)
        => CreateAuthor(root, new X500DistinguishedName(subject), expired, timestamping, keySize, notBefore, notAfter,
            lifetimeSigning, revocationUrl, weakCertificateSignature);

    private static X509Certificate2 CreateAuthor(X509Certificate2 root, X500DistinguishedName subject, bool expired = false,
        bool timestamping = false, int keySize = 2048, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null,
        bool lifetimeSigning = false, string? revocationUrl = null, bool weakCertificateSignature = false)
    {
        using var key = RSA.Create(keySize);
        CertificateRequest request = new(subject, key,
            weakCertificateSignature ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        OidCollection purposes = [new Oid(timestamping ? "1.3.6.1.5.5.7.3.8" : "1.3.6.1.5.5.7.3.3")];
        if (lifetimeSigning)
        {
            purposes.Add(new Oid("1.3.6.1.4.1.311.10.3.13"));
        }

        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes, true));
        if (revocationUrl is not null)
        {
            request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([revocationUrl]));
        }
        DateTimeOffset start = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset end = notAfter ?? (expired ? DateTimeOffset.UtcNow.AddHours(-12) : DateTimeOffset.UtcNow.AddDays(1));
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        using X509Certificate2 certificate = weakCertificateSignature
            ? request.Create(root.SubjectName, new Sha1CertificateSignatureGenerator(root), start, end, serial)
            : request.Create(root, start, end, serial);
        return certificate.CopyWithPrivateKey(key);
    }

    private static async Task<string> SignAsync(string path, X509Certificate2 root, X509Certificate2 author,
        X509Certificate2? timestamper = null, X509Certificate2? timestampRoot = null, bool wrongImprint = false,
        bool repository = false, bool corruptSignature = false, int timestampCount = 1, DateTimeOffset? timestampTime = null,
        bool weakTimestampDigest = false, bool weakTimestampSignatureDigest = false)
    {
        string signed = path + ".signed.nupkg";
        // NuGet requests own their certificates; preserve the caller's signer for additional packages.
        using X509Certificate2 requestCertificate = new(author);
        using SignPackageRequest request = repository
            ? new RepositorySignPackageRequest(requestCertificate, NuGet.Common.HashAlgorithmName.SHA256,
                NuGet.Common.HashAlgorithmName.SHA256, new Uri("https://example.test/v3/index.json"), ["test"])
            : new AuthorSignPackageRequest(requestCertificate, NuGet.Common.HashAlgorithmName.SHA256);
        await using FileStream input = File.OpenRead(path);
        await using FileStream output = File.Create(signed);
        using SignedPackageArchive archive = new(input, output);
        byte[] hash = await archive.GetArchiveHashAsync(NuGet.Common.HashAlgorithmName.SHA256, CancellationToken.None);
        SignatureContent content = new(SigningSpecifications.V1, NuGet.Common.HashAlgorithmName.SHA256, Convert.ToBase64String(hash));
        SignedCms cms = new(new ContentInfo(content.GetBytes()));
        CmsSigner signer = new(SubjectIdentifierType.IssuerAndSerialNumber, author)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
        };
        signer.Certificates.Add(root);
        CryptographicAttributeObjectCollection attributes = request is RepositorySignPackageRequest repositoryRequest
            ? SigningUtility.CreateSignedAttributes(repositoryRequest, [author, root])
            : SigningUtility.CreateSignedAttributes(request, [author, root]);
        foreach (CryptographicAttributeObject attribute in attributes)
        {
            signer.SignedAttributes.Add(attribute);
        }

        cms.ComputeSignature(signer);
        for (int index = 0; timestamper is not null && index < timestampCount; index++)
        {
            byte[] signatureValue = wrongImprint ? [1, 2, 3] : cms.SignerInfos[0].GetSignature();
            byte[] imprint = weakTimestampDigest ? SHA1.HashData(signatureValue) : SHA256.HashData(signatureValue);
            string digestOid = weakTimestampDigest ? "1.3.14.3.2.26" : "2.16.840.1.101.3.4.2.1";
            Rfc3161TimestampTokenInfo token = new(new Oid("1.2.3.4"), new Oid(digestOid),
                imprint, new byte[] { (byte)(index + 1) }, timestampTime ?? DateTimeOffset.UtcNow.AddHours(-18), null, false, null, null, null);
            SignedCms timestampCms = new(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), token.Encode()));
            CmsSigner timestampSigner = new(SubjectIdentifierType.IssuerAndSerialNumber, timestamper)
            {
                IncludeOption = X509IncludeOption.EndCertOnly,
                DigestAlgorithm = new Oid(weakTimestampSignatureDigest ? "1.3.14.3.2.26" : "2.16.840.1.101.3.4.2.1"),
            };
            timestampSigner.Certificates.Add(timestampRoot!);
            using X509Certificate2 timestampCertificate = new(timestamper);
            using AuthorSignPackageRequest timestampRequest = new(timestampCertificate, NuGet.Common.HashAlgorithmName.SHA256);
            foreach (CryptographicAttributeObject attribute in SigningUtility.CreateSignedAttributes(timestampRequest, [timestamper, timestampRoot!]))
            {
                if (attribute.Oid.Value == "1.2.840.113549.1.9.16.2.47")
                {
                    timestampSigner.SignedAttributes.Add(attribute);
                }
            }

            timestampCms.ComputeSignature(timestampSigner);
            cms.SignerInfos[0].AddUnsignedAttribute(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.14"), timestampCms.Encode()));
        }

        byte[] signatureBytes = cms.Encode();
        if (corruptSignature)
        {
            signatureBytes[^1] ^= 1;
        }

        using MemoryStream signature = new(signatureBytes);
        await archive.AddSignatureAsync(signature, CancellationToken.None);
        return signed;
    }

    private sealed class TestRootTrust(X509Certificate2 root, Action? onLoad = null, string? trustedPublisherFingerprint = null) : WorkloadPackageTrust
    {
        protected override PackageSignatureVerifier CreateVerifier(PrimarySignature signature)
        {
            onLoad?.Invoke();
            signature.SignedCms.Certificates.Cast<X509Certificate2>()
                .Should().Contain(certificate => certificate.RawData.SequenceEqual(root.RawData));
            string signerFingerprint = signature.SignerInfo.Certificate!.GetCertHashString(HashAlgorithmName.SHA256);
            string publisherFingerprint = trustedPublisherFingerprint ?? signerFingerprint;
            KeyValuePair<string, NuGet.Common.HashAlgorithmName>[] untrustedRootFingerprints =
            [
                new(signerFingerprint, NuGet.Common.HashAlgorithmName.SHA256),
                .. signature.Timestamps.Select(timestamp =>
                    new KeyValuePair<string, NuGet.Common.HashAlgorithmName>(
                        timestamp.SignerInfo.Certificate!.GetCertHashString(HashAlgorithmName.SHA256),
                        NuGet.Common.HashAlgorithmName.SHA256)),
            ];
            return new([
                new IntegrityVerificationProvider(),
                new SignatureTrustAndValidityVerificationProvider(untrustedRootFingerprints),
                new AllowListVerificationProvider(
                    [new CertificateHashAllowListEntry(
                        VerificationTarget.Author,
                        SignaturePlacement.PrimarySignature,
                        publisherFingerprint,
                        NuGet.Common.HashAlgorithmName.SHA256)],
                    requireNonEmptyAllowList: true),
            ]);
        }

        internal override SignedPackageVerifierSettings CreateVerifierSettings()
            => new(
                allowUnsigned: false,
                allowIllegal: false,
                allowUntrusted: false,
                allowIgnoreTimestamp: false,
                allowMultipleTimestamps: false,
                allowNoTimestamp: true,
                allowUnknownRevocation: true,
                reportUnknownRevocation: false,
                verificationTarget: VerificationTarget.Author,
                signaturePlacement: SignaturePlacement.PrimarySignature,
                repositoryCountersignatureVerificationBehavior: SignatureVerificationBehavior.Never,
                revocationMode: NuGet.Common.RevocationMode.Offline);
    }
}
