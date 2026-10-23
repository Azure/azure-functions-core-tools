// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Azure.Functions.Cli.Workloads.Discovery;
using HashAlgorithmName = System.Security.Cryptography.HashAlgorithmName;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed partial class WorkloadPackageTrustTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("timestamped-expired", true)]
    [InlineData("corrupt-signature", false)]
    [InlineData("tampered-content", false)]
    [InlineData("weak-key", false)]
    [InlineData("weak-certificate-signature", false)]
    [InlineData("lifetime-signing", false)]
    [InlineData("wrong-identity", false)]
    [InlineData("expired", false)]
    [InlineData("future", false)]
    [InlineData("wrong-purpose", false)]
    [InlineData("unapproved-root", false)]
    [InlineData("bad-timestamp", false)]
    public async Task WithoutOfflineRevocation_AuthorPolicyIsPreserved(string scenario, bool accepted)
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 otherRoot = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root,
            scenario == "wrong-identity" ? "CN=Not Microsoft" : MicrosoftSubject,
            expired: scenario is "expired" or "timestamped-expired",
            timestamping: scenario == "wrong-purpose",
            keySize: scenario == "weak-key" ? 1024 : 2048,
            notBefore: scenario == "future" ? DateTimeOffset.UtcNow.AddHours(1) : null,
            lifetimeSigning: scenario == "lifetime-signing",
            weakCertificateSignature: scenario == "weak-certificate-signature");
        using X509Certificate2 timestamper = CreateAuthor(root, "CN=Test Timestamp Authority", timestamping: true);
        bool hasTimestamp = scenario is "timestamped-expired" or "bad-timestamp";
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author,
            hasTimestamp ? timestamper : null, hasTimestamp ? root : null,
            wrongImprint: scenario == "bad-timestamp", corruptSignature: scenario == "corrupt-signature");
        if (scenario == "tampered-content")
        {
            using ZipArchive archive = ZipFile.Open(signed, ZipArchiveMode.Update);
            using StreamWriter writer = new(archive.CreateEntry("tampered.txt").Open());
            writer.Write("unsigned change");
        }

        TestRootTrust trust = new(scenario == "unapproved-root" ? otherRoot : root, supportsOfflineRevocation: false);
        Func<Task> act = async () =>
        {
            using var snapshot = await trust.AcquireAsync(signed, false, TestContextToken());
            File.Exists(snapshot.Path).Should().BeTrue();
        };

        if (accepted)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
        }

        File.Exists(signed).Should().BeTrue();
    }

    [Fact]
    public async Task WithoutOfflineRevocation_DoesNotContactCertificateEndpoints()
    {
        await using RevocationEndpoint endpoint = new();
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 control = CreateAuthor(root, MicrosoftSubject, revocationUrl: endpoint.Address + "control.crl");
        using X509Chain online = new();
        online.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        online.ChainPolicy.CustomTrustStore.Add(root);
        online.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        online.ChainPolicy.RevocationFlag = X509RevocationFlag.EndCertificateOnly;
        online.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
        online.Build(control).Should().BeFalse();
        endpoint.Requests.Should().Contain("/control.crl", "the online control must actually reach the revocation endpoint");

        // Distinct certificate endpoints keep the control's cache from masking a verifier regression.
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject, revocationUrl: endpoint.Address + "author.crl");
        using X509Certificate2 timestamper = CreateAuthor(root, "CN=Test Timestamp Authority",
            timestamping: true, revocationUrl: endpoint.Address + "timestamp.crl");
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author, timestamper, root);
        TestRootTrust trust = new(root, supportsOfflineRevocation: OperatingSystem.IsMacOS() ? null : false);
        using (var snapshot = await trust.AcquireAsync(signed, false, TestContextToken()))
        {
            File.Exists(snapshot.Path).Should().BeTrue();
        }

        endpoint.Requests.Should().OnlyContain(path => path == "/control.crl");
    }

    private sealed class Sha1CertificateSignatureGenerator(X509Certificate2 issuer) : X509SignatureGenerator
    {
        protected override PublicKey BuildPublicKey() => issuer.PublicKey;

        public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm)
        {
            AsnWriter writer = new(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteObjectIdentifier("1.2.840.113549.1.1.5");
            writer.WriteNull();
            writer.PopSequence();
            return writer.Encode();
        }

        public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
        {
            using RSA key = issuer.GetRSAPrivateKey()!;
            return key.SignData(data, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
    }

    private sealed class RevocationEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;

        public RevocationEndpoint()
        {
            _listener.Start();
            Address = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _server = ServeAsync(_stop.Token);
        }

        public string Address { get; }

        public ConcurrentQueue<string> Requests { get; } = new();

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _server;
            }
            catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or SocketException)
            {
                // Stopping the test listener interrupts its pending accept/read.
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                await using NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                string request = await reader.ReadLineAsync(cancellationToken) ?? throw new InvalidDataException("Missing CRL request.");
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
                {
                }

                Requests.Enqueue(request.Split(' ')[1]);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cancellationToken);
            }
        }
    }
}
