// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Azure.Functions.Cli.Workloads.Install.Trust;

/// <summary>
/// <see cref="ITrustAnchorStore"/> backed by an embedded PEM bundle
/// (<c>trusted-roots.pem</c>) shipped inside the CLI assembly. Supports
/// concatenated PEM-encoded X.509 certificates, the standard cert-bundle
/// format used by tools like cURL and OpenSSL.
/// </summary>
internal sealed class EmbeddedTrustAnchorStore : ITrustAnchorStore
{
    internal const string ResourceName = "Azure.Functions.Cli.Workloads.Install.Trust.trusted-roots.pem";

    private readonly Lazy<X509Certificate2Collection> _anchors;

    public EmbeddedTrustAnchorStore()
        : this(typeof(EmbeddedTrustAnchorStore).Assembly)
    {
    }

    internal EmbeddedTrustAnchorStore(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _anchors = new Lazy<X509Certificate2Collection>(() => LoadFromAssembly(assembly));
    }

    /// <inheritdoc />
    public X509Certificate2Collection GetTrustAnchors() => _anchors.Value;

    private static X509Certificate2Collection LoadFromAssembly(Assembly assembly)
    {
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded trust bundle '{ResourceName}' was not found. " +
                "This indicates a packaging bug; reinstall the CLI.");
        }

        using StreamReader reader = new(stream);
        string pem = reader.ReadToEnd();

        var collection = new X509Certificate2Collection();
        if (string.IsNullOrWhiteSpace(pem) || !pem.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            // Empty / placeholder bundle. Return an empty collection rather
            // than throwing; the verifier surfaces a user-facing error when
            // it sees no anchors, which is friendlier than an InvalidOp here.
            return collection;
        }

        try
        {
            collection.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            throw new InvalidOperationException(
                $"Embedded trust bundle '{ResourceName}' is malformed: {ex.Message}",
                ex);
        }

        return collection;
    }
}
