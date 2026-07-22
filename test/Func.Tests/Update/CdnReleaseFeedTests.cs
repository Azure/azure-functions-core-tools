// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Reflection;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.Logging.Abstractions;
using Semver;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class CdnReleaseFeedTests
{
    private const string FixtureResource = "Azure.Functions.Cli.Tests.Update.Fixtures.version.json";

    [Fact]
    public async Task GetLatestAsync_StableOnly_ReturnsStableVersion()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithManifest());

        Release latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        latest.Version.ToString().Should().Be("5.1.0");
        latest.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public async Task GetLatestAsync_IncludePrerelease_ReturnsHigherOfStableAndPreview()
    {
        // Fixture: stable=5.1.0, preview=5.2.0-preview.1
        // 5.2.0-preview.1 > 5.1.0 by SemVer precedence, so preview wins.
        CdnReleaseFeed feed = CreateFeed(RespondWithManifest());

        Release latest = await feed.GetLatestAsync(includePrerelease: true, CancellationToken.None);

        latest.Version.ToString().Should().Be("5.2.0-preview.1");
        latest.IsPrerelease.Should().BeTrue();
    }

    [Fact]
    public async Task GetLatestAsync_IncludePrerelease_ReturnsStableWhenHigherThanPreview()
    {
        // stable=5.3.0, preview=5.2.0-preview.1 → stable wins
        CdnReleaseFeed feed = CreateFeed(RespondWithJson("""{"stable": "5.3.0", "preview": "5.2.0-preview.1"}"""));

        Release latest = await feed.GetLatestAsync(includePrerelease: true, CancellationToken.None);

        latest.Version.ToString().Should().Be("5.3.0");
        latest.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public async Task GetLatestAsync_IncludePrerelease_NullPreview_ReturnsStable()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithJson("""{"stable": "5.1.0", "preview": null}"""));

        Release latest = await feed.GetLatestAsync(includePrerelease: true, CancellationToken.None);

        latest.Version.ToString().Should().Be("5.1.0");
    }

    [Fact]
    public async Task GetLatestAsync_StableOnly_MissingStable_Throws()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithJson("""{"stable": null, "preview": "5.2.0-preview.1"}"""));

        InvalidOperationException ex = (await FluentActions.Awaiting(() => feed.GetLatestAsync(includePrerelease: false, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>()).Which;

        ex.Message.Should().Contain("version is missing");
    }

    [Fact]
    public async Task GetLatestAsync_UnparseableStable_Throws()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithJson("""{"stable": "not-a-version", "preview": null}"""));

        InvalidOperationException ex = (await FluentActions.Awaiting(() => feed.GetLatestAsync(includePrerelease: false, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>()).Which;

        ex.Message.Should().Contain("invalid version");
    }

    [Fact]
    public async Task GetLatestAsync_DownloadUrlIsRelativeUri()
    {
        CdnReleaseFeed feed = CreateFeed(RespondWithManifest());

        Release latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        latest.DownloadUrl.ToString().Should().Contain("5.1.0");
        latest.DownloadUrl.ToString().Should().Contain("Azure.Functions.Cli.");
        latest.DownloadUrl.ToString().Should().EndWith(".zip");
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

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        CdnReleaseFeed feed = CreateFeed(handler);
        var target = SemVersion.Parse("5.0.0-preview.1", SemVersionStyles.Strict);

        Release result = await feed.GetVersionAsync(target, CancellationToken.None);

        result.Version.ToString().Should().Be("5.0.0-preview.1");
        result.IsPrerelease.Should().BeTrue();
    }

    [Fact]
    public async Task GetVersionAsync_NotOnCdn_Throws()
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

        InvalidOperationException ex = (await FluentActions.Awaiting(() => feed.GetVersionAsync(target, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>()).Which;

        ex.Message.Should().ContainEquivalentOf("not found");
    }

    [Fact]
    public async Task GetVersionAsync_ServerError_Throws()
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

        InvalidOperationException ex = (await FluentActions.Awaiting(() => feed.GetVersionAsync(target, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>()).Which;

                ex.Message.Should().Contain("ServiceUnavailable");
    }

    [Fact]
    public async Task FetchManifest_NotFound_Throws()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        CdnReleaseFeed feed = CreateFeed(handler);

        InvalidOperationException ex = (await FluentActions.Awaiting(() => feed.GetLatestAsync(includePrerelease: false, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>()).Which;

        ex.Message.Should().Contain("version.json");
    }

    [Fact]
    public async Task FetchManifest_ServerError_Throws()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        CdnReleaseFeed feed = CreateFeed(handler);

        InvalidOperationException ex = (await FluentActions.Awaiting(() => feed.GetLatestAsync(includePrerelease: false, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>()).Which;

                ex.Message.Should().Contain("ServiceUnavailable");
    }

    [Fact]
    public async Task GetLatestAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        var handler = new StubHttpMessageHandler(RespondWithManifest());
        CdnReleaseFeed feed = CreateFeed(handler);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions.Awaiting(() => feed.GetLatestAsync(includePrerelease: false, cts.Token)).Should().ThrowAsync<TaskCanceledException>();
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
        var client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://cdn.functions.azure.com/"),
        };

        return new CdnReleaseFeed(client, NullLogger<CdnReleaseFeed>.Instance);
    }
}
