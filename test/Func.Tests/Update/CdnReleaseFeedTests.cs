// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Reflection;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Semver;
using Xunit;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class CdnReleaseFeedTests
{
    private const string FixtureResource = "Azure.Functions.Cli.Tests.Update.Fixtures.version.json";

    [Fact]
    public async Task GetLatestAsync_StableOnly_ReturnsStableVersion()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithManifest());

        Release? latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal("5.1.0", latest!.Version.ToString());
        Assert.False(latest.IsPrerelease);
    }

    [Fact]
    public async Task GetLatestAsync_IncludingPrerelease_ReturnsPreviewVersion()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithManifest());

        Release? latest = await feed.GetLatestAsync(includePrerelease: true, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal("5.2.0-preview.1", latest!.Version.ToString());
        Assert.True(latest.IsPrerelease);
    }

    [Fact]
    public async Task GetLatestAsync_NullStableInManifest_ReturnsNull()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithJson("""{"stable": null, "preview": "5.2.0-preview.1"}"""));

        Release? latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.Null(latest);
    }

    [Fact]
    public async Task GetLatestAsync_NullPreviewInManifest_ReturnsNull()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithJson("""{"stable": "5.1.0", "preview": null}"""));

        Release? latest = await feed.GetLatestAsync(includePrerelease: true, CancellationToken.None);

        Assert.Null(latest);
    }

    [Fact]
    public async Task GetLatestAsync_UnparseableVersion_ReturnsNull()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithJson("""{"stable": "not-a-version", "preview": null}"""));

        Release? latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.Null(latest);
    }

    [Fact]
    public async Task GetLatestAsync_DownloadUrlContainsVersionAndRid()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithManifest());

        Release? latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Contains("5.1.0", latest!.DownloadUrl, StringComparison.Ordinal);
        Assert.Contains("Azure.Functions.Cli.", latest.DownloadUrl, StringComparison.Ordinal);
        Assert.EndsWith(".zip", latest.DownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVersionAsync_ExistsOnCdn_ReturnsRelease()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("version.json", StringComparison.Ordinal) == true)
            {
                return MakeJsonResponse(LoadFixture());
            }

            // HEAD request for the artifact returns 200
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        CdnReleaseFeed feed = CreateFeed(handler);
        var target = SemVersion.Parse("5.0.0-preview.1", SemVersionStyles.Strict);

        Release? result = await feed.GetVersionAsync(target, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("5.0.0-preview.1", result!.Version.ToString());
        Assert.True(result.IsPrerelease);
    }

    [Fact]
    public async Task GetVersionAsync_NotOnCdn_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("version.json", StringComparison.Ordinal) == true)
            {
                return MakeJsonResponse(LoadFixture());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        CdnReleaseFeed feed = CreateFeed(handler);
        var target = SemVersion.Parse("99.0.0", SemVersionStyles.Strict);

        Release? result = await feed.GetVersionAsync(target, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersionAsync_ServerError_ThrowsGraceful()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("version.json", StringComparison.Ordinal) == true)
            {
                return MakeJsonResponse(LoadFixture());
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        CdnReleaseFeed feed = CreateFeed(handler);
        var target = SemVersion.Parse("5.0.0", SemVersionStyles.Strict);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => feed.GetVersionAsync(target, CancellationToken.None));

        Assert.Contains("503", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchManifest_NotFound_ThrowsGracefulMentioningFeed()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        CdnReleaseFeed feed = CreateFeed(handler);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => feed.GetLatestAsync(includePrerelease: false, CancellationToken.None));

        Assert.Contains("manifest", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchManifest_ServerError_ThrowsGraceful()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        CdnReleaseFeed feed = CreateFeed(handler);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => feed.GetLatestAsync(includePrerelease: false, CancellationToken.None));

        Assert.Contains("503", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLatestAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        var handler = new StubHttpMessageHandler(RespondWithManifest());
        CdnReleaseFeed feed = CreateFeed(handler);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => feed.GetLatestAsync(includePrerelease: false, cts.Token));
    }

    private static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> RespondWithManifest()
    {
        byte[] body = LoadFixture();
        return (_, _) => MakeJsonResponse(body);
    }

    private static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> RespondWithJson(string json)
    {
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        return (_, _) => MakeJsonResponse(body);
    }

    private static HttpResponseMessage MakeJsonResponse(byte[] body) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(body)
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") },
        },
    };

    private static byte[] LoadFixture()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FixtureResource)
            ?? throw new InvalidOperationException($"Embedded fixture '{FixtureResource}' not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static CdnReleaseFeed CreateFeed(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        return CreateFeed(handler);
    }

    private static CdnReleaseFeed CreateFeed(StubHttpMessageHandler handler)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(CdnReleaseFeed.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://cdn.functions.azure.com/"),
        });

        return new CdnReleaseFeed(factory, NullLogger<CdnReleaseFeed>.Instance);
    }
}
