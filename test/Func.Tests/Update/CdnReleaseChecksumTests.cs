// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.Logging.Abstractions;
using Semver;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class CdnReleaseChecksumTests
{
    private static readonly string _digest = new('a', 64);
    private static readonly SemVersion _version = SemVersion.Parse("5.1.0", SemVersionStyles.Strict);
    private static readonly bool[] _pinnedCases = [false, true];

    public static IEnumerable<object[]> InvalidSidecars()
    {
        string[] bodies =
        [
            string.Empty,
            new string('a', 63) + "  {filename}",
            new string('a', 65) + "  {filename}",
            new string('g', 64) + "  {filename}",
            _digest,
            _digest + " {filename}",
            _digest + " *{filename}",
            _digest + "  wrong.zip",
            _digest + "  ../{filename}",
            _digest + "  {filename}\n" + _digest + "  {filename}",
        ];
        foreach (bool pinned in _pinnedCases)
        {
            foreach (string body in bodies)
            {
                yield return [pinned, body];
            }
        }
    }

    public static IEnumerable<object[]> SidecarFailures()
    {
        string[] kinds = ["http", "io", "timeout"];
        int[] stages = [0, 1, 2];
        foreach (bool pinned in _pinnedCases)
        {
            foreach (string kind in kinds)
            {
                foreach (int stage in stages)
                {
                    // IOException is a stream failure, not an HttpClient request failure.
                    if (kind != "io" || stage != 0)
                    {
                        yield return [pinned, kind, stage];
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(false, false, "5.1.0", "")]
    [InlineData(false, true, "5.2.0-preview.1", "\n")]
    [InlineData(true, false, "5.1.0", "\r\n")]
    public async Task Discovery_ValidSidecar_BindsChecksumToSelectedArtifact(
        bool pinned, bool preview, string expectedVersion, string newline)
    {
        List<string> sidecarRequests = [];
        CdnReleaseFeed feed = CreateFeed(request =>
        {
            sidecarRequests.Add(request.RequestUri!.AbsolutePath);
            return Response($"{_digest.ToUpperInvariant()}  {ArtifactFileName(request)}{newline}");
        });

        Release release = pinned
            ? await feed.GetVersionAsync(_version, CancellationToken.None)
            : await feed.GetLatestAsync(preview, CancellationToken.None);

        release.Version.ToString().Should().Be(expectedVersion);
        release.Sha256Checksum.Should().Be(_digest.ToUpperInvariant());
        sidecarRequests.Should().ContainSingle()
            .Which.Should().Be($"/public/cli/v5/{expectedVersion}/Azure.Functions.Cli.{RuntimeInformation.RuntimeIdentifier}.{expectedVersion}.{Release.ArchiveExtension}.sha256");
    }

    [Theory]
    [InlineData(false, HttpStatusCode.NotFound)]
    [InlineData(true, HttpStatusCode.NotFound)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    public async Task Discovery_SidecarUnavailable_FailsClosed(bool pinned, HttpStatusCode status)
    {
        CdnReleaseFeed feed = CreateFeed(_ => new HttpResponseMessage(status));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DiscoverAsync(feed, pinned, CancellationToken.None));

        ex.Message.Should().Contain(".sha256").And.Contain(status.ToString()).And.Contain("Try again");
    }

    [Theory]
    [MemberData(nameof(InvalidSidecars))]
    public async Task Discovery_InvalidSidecar_FailsClosed(bool pinned, string body)
    {
        CdnReleaseFeed feed = CreateFeed(request => Response(
            body.Replace("{filename}", ArtifactFileName(request), StringComparison.Ordinal)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DiscoverAsync(feed, pinned, CancellationToken.None));

        ex.Message.Should().Contain("Invalid checksum sidecar").And.Contain("cannot be verified").And.Contain("Try again");
    }

    [Theory]
    [MemberData(nameof(SidecarFailures))]
    public async Task Discovery_SidecarFailure_TranslatesWithInnerException(bool pinned, string kind, int stage)
    {
        Exception failure = kind switch
        {
            "http" => new HttpRequestException("offline"),
            "io" => new IOException("connection reset"),
            _ => new OperationCanceledException("timeout"),
        };
        CdnReleaseFeed feed = CreateFeed(_ => stage == 0
            ? throw failure
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingHttpContent(failure, stage == 1) });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DiscoverAsync(feed, pinned, CancellationToken.None));

        ex.InnerException.Should().BeSameAs(failure);
        ex.Message.Should().Contain(".sha256").And.Contain("Check your connection");
        if (kind == "timeout")
        {
            ex.Message.Should().Contain("Timed out");
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Discovery_CallerCancelsSidecar_PropagatesCancellation(bool pinned, int stage)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException(cancellation.Token);
        CdnReleaseFeed feed = CreateFeed(_ =>
        {
            if (stage == 0)
            {
                cancellation.Cancel();
                throw failure;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new FailingHttpContent(failure, stage == 1, cancellation.Cancel),
            };
        });

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiscoverAsync(feed, pinned, cancellation.Token));

        ex.CancellationToken.Should().Be(cancellation.Token);
    }

    private static Task<Release> DiscoverAsync(CdnReleaseFeed feed, bool pinned, CancellationToken cancellationToken)
        => pinned ? feed.GetVersionAsync(_version, cancellationToken) : feed.GetLatestAsync(false, cancellationToken);

    private static string ArtifactFileName(HttpRequestMessage request)
        => Path.GetFileName(request.RequestUri!.AbsolutePath)[..^".sha256".Length];

    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static CdnReleaseFeed CreateFeed(Func<HttpRequestMessage, HttpResponseMessage> sidecarResponse)
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("version.json", StringComparison.Ordinal))
            {
                return Response("""{"stable":"5.1.0","preview":"5.2.0-preview.1"}""");
            }

            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri.AbsolutePath.Should().EndWith(".sha256");
            return sidecarResponse(request);
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://cdn.functions.azure.com/") };
        return new CdnReleaseFeed(client, NullLogger<CdnReleaseFeed>.Instance);
    }
}
