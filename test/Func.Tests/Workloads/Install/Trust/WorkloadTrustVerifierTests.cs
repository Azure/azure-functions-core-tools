// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Functions.Cli.Workloads.Install.Trust;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Install.Trust;

public sealed class WorkloadTrustVerifierTests : IDisposable
{
    private readonly string _root;
    private readonly ITrustAnchorStore _store = Substitute.For<ITrustAnchorStore>();

    public WorkloadTrustVerifierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"func-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _store.GetTrustAnchors().Returns([]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup; surfacing this would mask the real failure
        }
    }

    [Fact]
    public async Task AllowUntrusted_ShortCircuits_Trusted()
    {
        var verifier = new WorkloadTrustVerifier(_store);

        TrustVerificationResult result = await verifier.VerifyAsync(
            "/nonexistent.nupkg", "anything", allowUntrusted: true);

        Assert.True(result.IsTrusted);
        _store.DidNotReceiveWithAnyArgs().GetTrustAnchors();
    }

    [Fact]
    public async Task NonOfficialPrefix_Untrusted()
    {
        var verifier = new WorkloadTrustVerifier(_store);

        TrustVerificationResult result = await verifier.VerifyAsync(
            "/nonexistent.nupkg", "contoso.totally.legit", allowUntrusted: false);

        Assert.False(result.IsTrusted);
        Assert.Contains("not part of the Azure Functions CLI workload namespace", result.Reason);
    }

    [Fact]
    public async Task NoTrustAnchorsConfigured_Untrusted()
    {
        var verifier = new WorkloadTrustVerifier(_store);

        TrustVerificationResult result = await verifier.VerifyAsync(
            "/nonexistent.nupkg", "azure.functions.cli.workloads.node", allowUntrusted: false);

        Assert.False(result.IsTrusted);
        Assert.Contains("No trust anchors", result.Reason);
    }

    [Fact]
    public async Task UnsignedPackage_Untrusted()
    {
        using X509Certificate2 anchor = CreateSelfSignedRoot("CN=Test Anchor");
        _store.GetTrustAnchors().Returns([anchor]);

        string nupkg = BuildUnsignedNupkg();
        var verifier = new WorkloadTrustVerifier(_store);

        TrustVerificationResult result = await verifier.VerifyAsync(
            nupkg, "azure.functions.cli.workloads.node", allowUntrusted: false);

        Assert.False(result.IsTrusted);
        Assert.Contains("not signed", result.Reason);
    }

    [Fact]
    public async Task NullOrEmptyArguments_Throw()
    {
        var verifier = new WorkloadTrustVerifier(_store);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => verifier.VerifyAsync(null!, "id", false));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => verifier.VerifyAsync("/x.nupkg", "", false));
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkloadTrustVerifier(null!));
    }

    [Fact]
    public void IsChainTrustedTo_LeafChainsToBundledRoot_Trusted()
    {
        using X509Certificate2 root = CreateSelfSignedRoot("CN=Test Root");
        using X509Certificate2 leaf = CreateLeafSignedBy(root, "CN=Test Leaf");

        bool trusted = WorkloadTrustVerifier.IsChainTrustedTo(
            leaf,
            anchors: [root],
            extraStore: [],
            out string failure);

        Assert.True(trusted, $"Expected chain to validate, got: {failure}");
        Assert.Empty(failure);
    }

    [Fact]
    public void IsChainTrustedTo_LeafDoesNotChainToBundledRoot_Untrusted()
    {
        using X509Certificate2 trustedRoot = CreateSelfSignedRoot("CN=Trusted Root");
        using X509Certificate2 untrustedRoot = CreateSelfSignedRoot("CN=Untrusted Root");
        using X509Certificate2 leaf = CreateLeafSignedBy(untrustedRoot, "CN=Foreign Leaf");

        bool trusted = WorkloadTrustVerifier.IsChainTrustedTo(
            leaf,
            anchors: [trustedRoot],
            extraStore: [],
            out string failure);

        Assert.False(trusted);
        Assert.NotEmpty(failure);
    }

    [Fact]
    public void IsChainTrustedTo_NullArgs_Throw()
    {
        using X509Certificate2 root = CreateSelfSignedRoot("CN=Root");
        Assert.Throws<ArgumentNullException>(() =>
            WorkloadTrustVerifier.IsChainTrustedTo(null!, [], [], out _));
        Assert.Throws<ArgumentNullException>(() =>
            WorkloadTrustVerifier.IsChainTrustedTo(root, null!, [], out _));
        Assert.Throws<ArgumentNullException>(() =>
            WorkloadTrustVerifier.IsChainTrustedTo(root, [], null!, out _));
    }

    private static X509Certificate2 CreateSelfSignedRoot(string subject)
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature, critical: true));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static X509Certificate2 CreateLeafSignedBy(X509Certificate2 issuer, string subject)
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        byte[] serial = new byte[8];
        RandomNumberGenerator.Fill(serial);

        return req.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(15), serial);
    }

    // Minimal .nupkg synthesis: just a zip with a .nuspec. NuGet's
    // PackageArchiveReader is happy to open it; IsSignedAsync returns false
    // because there's no .signature.p7s entry. That's all we need to exercise
    // the "unsigned package" branch without dragging in a real signing cert.
    private string BuildUnsignedNupkg()
    {
        string path = Path.Combine(_root, $"unsigned-{Guid.NewGuid():N}.nupkg");
        using (FileStream stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("test.nuspec");
            using StreamWriter writer = new(entry.Open());
            writer.Write("""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>azure.functions.cli.workloads.node</id>
                    <version>1.0.0</version>
                    <authors>test</authors>
                    <description>For tests.</description>
                  </metadata>
                </package>
                """);
        }

        return path;
    }
}
