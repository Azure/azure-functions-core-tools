// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Azure.Functions.Cli.Workloads.Install.Trust;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Install.Trust;

public sealed class EmbeddedTrustAnchorStoreTests
{
    [Fact]
    public void DefaultConstructor_LoadsBundledResource_DoesNotThrow()
    {
        var store = new EmbeddedTrustAnchorStore();

        X509Certificate2Collection anchors = store.GetTrustAnchors();

        // The shipped bundle may be empty (placeholder) or populated. Either is
        // a valid load; we only assert the call returns without throwing.
        Assert.NotNull(anchors);
    }

    [Fact]
    public void GetTrustAnchors_CachesAcrossCalls()
    {
        var store = new EmbeddedTrustAnchorStore();

        X509Certificate2Collection first = store.GetTrustAnchors();
        X509Certificate2Collection second = store.GetTrustAnchors();

        Assert.Same(first, second);
    }

    [Fact]
    public void Constructor_NullAssembly_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EmbeddedTrustAnchorStore(null!));
    }

    [Fact]
    public void GetTrustAnchors_MissingResource_Throws()
    {
        var store = new EmbeddedTrustAnchorStore(typeof(string).Assembly);

        Assert.Throws<InvalidOperationException>(store.GetTrustAnchors);
    }

    [Fact]
    public void GetTrustAnchors_PopulatedBundle_LoadsCertificate()
    {
        Assembly assembly = AssemblyWithPemResource(MakeSelfSignedPem());

        var store = new EmbeddedTrustAnchorStore(assembly);
        var anchors = store.GetTrustAnchors();

        Assert.Single(anchors);
        Assert.Contains("Test Bundled Root", anchors[0].Subject);
    }

    [Fact]
    public void GetTrustAnchors_EmptyBundle_ReturnsEmpty()
    {
        // Placeholder bundles ship with a comment-only PEM file before the
        // real cert is added. The store treats that as "no anchors" rather
        // than blowing up.
        Assembly assembly = AssemblyWithPemResource("# placeholder, no certs yet\n");

        var store = new EmbeddedTrustAnchorStore(assembly);
        var anchors = store.GetTrustAnchors();

        Assert.Empty(anchors);
    }

    private static string MakeSelfSignedPem()
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=Test Bundled Root", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using X509Certificate2 cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return cert.ExportCertificatePem();
    }

    private static Assembly AssemblyWithPemResource(string pem)
    {
        // Stand up an in-memory assembly that ships the embedded PEM under
        // the exact logical name the store expects. Lets us exercise the
        // happy/malformed paths without touching the real bundle file.
        var bytes = Encoding.UTF8.GetBytes(pem);
        return new InMemoryAssemblyWithResource(EmbeddedTrustAnchorStore.ResourceName, bytes);
    }

    private sealed class InMemoryAssemblyWithResource(string resourceName, byte[] payload) : Assembly
    {
        public override Stream? GetManifestResourceStream(string name) =>
            name == resourceName ? new MemoryStream(payload, writable: false) : null;
    }
}
