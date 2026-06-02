// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Semver;
using Xunit;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class GitHubReleaseFeedTests
{
    private const string FixtureResource = "Azure.Functions.Cli.Tests.Update.Fixtures.releases.json";

    [Fact]
    public async Task GetLatestAsync_StableOnly_PrefersSemVerMaxStableIgnoringNewerPrereleases()
    {
        GitHubReleaseFeed feed = CreateFeed(out _, RespondWithFixture());

        Release? latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal("5.1.0", latest!.Version.ToString());
        Assert.False(latest.IsPrerelease);
    }

    [Fact]
    public async Task GetLatestAsync_IncludingPrereleases_ReturnsSemVerMaxAcrossAll()
    {
        GitHubReleaseFeed feed = CreateFeed(out _, RespondWithFixture());

        Release? latest = await feed.GetLatestAsync(includePrerelease: true, CancellationToken.None);

        Assert.NotNull(latest);

        // 5.1.0 (stable) > 5.0.0-preview.2 > 5.0.0-preview.1 > 5.0.0 (stable) > 4.9.0
        // per SemVer precedence: 5.1.0 wins.
        Assert.Equal("5.1.0", latest!.Version.ToString());
    }

    [Fact]
    public async Task GetLatestAsync_TagWithoutLeadingV_ParsesAndIsSelectable()
    {
        // The fixture's "5.1.0" entry has no leading "v"; it must still be picked
        // up by GetLatestAsync(false). Covered together with case 1's assertion,
        // but expressed separately so a regression surfaces with clear intent.
        GitHubReleaseFeed feed = CreateFeed(out _, RespondWithFixture());

        Release? latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal("5.1.0", latest!.TagName);
    }

    [Fact]
    public async Task GetLatestAsync_UnparseableTag_IsSkippedSilently()
    {
        // The fixture contains "release-2024-01" which is not strict SemVer 2.0.
        // It must be ignored without throwing.
        GitHubReleaseFeed feed = CreateFeed(out _, RespondWithFixture());

        Release? latest = await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.NotEqual("release-2024-01", latest!.TagName);
    }

    [Fact]
    public async Task GetVersionAsync_ExactMatch_ReturnsRelease()
    {
        GitHubReleaseFeed feed = CreateFeed(out _, RespondWithFixture());
        var target = SemVersion.Parse("5.0.0-preview.1", SemVersionStyles.Strict);

        Release? result = await feed.GetVersionAsync(target, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("v5.0.0-preview.1", result!.TagName);
        Assert.True(result.IsPrerelease);
    }

    [Fact]
    public async Task GetVersionAsync_NoMatch_ReturnsNull()
    {
        GitHubReleaseFeed feed = CreateFeed(out _, RespondWithFixture());
        var target = SemVersion.Parse("99.0.0", SemVersionStyles.Strict);

        Release? result = await feed.GetVersionAsync(target, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchReleases_GitHubTokenSet_AddsBearerAuthorizationHeader()
    {
        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("GITHUB_TOKEN").Returns("ghp_test-token-123");

        var handler = new StubHttpMessageHandler(RespondWithFixture());
        GitHubReleaseFeed feed = CreateFeed(handler, env);

        await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.NotNull(handler.LastRequest!.Headers.Authorization);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("ghp_test-token-123", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task FetchReleases_NoGitHubToken_OmitsAuthorizationHeader()
    {
        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("GITHUB_TOKEN").Returns((string?)null);

        var handler = new StubHttpMessageHandler(RespondWithFixture());
        GitHubReleaseFeed feed = CreateFeed(handler, env);

        await feed.GetLatestAsync(includePrerelease: false, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task FetchReleases_RateLimited_ThrowsGracefulMentioningGitHubToken()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("rate limit exceeded"),
            };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });

        GitHubReleaseFeed feed = CreateFeed(handler, Substitute.For<IProcessEnvironment>());

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => feed.GetLatestAsync(includePrerelease: false, CancellationToken.None));

        Assert.Contains("GITHUB_TOKEN", ex.Message, StringComparison.Ordinal);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task FetchReleases_ServerError_ThrowsGraceful()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        GitHubReleaseFeed feed = CreateFeed(handler, Substitute.For<IProcessEnvironment>());

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => feed.GetLatestAsync(includePrerelease: false, CancellationToken.None));

        Assert.Contains("503", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLatestAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        var handler = new StubHttpMessageHandler(RespondWithFixture());
        GitHubReleaseFeed feed = CreateFeed(handler, Substitute.For<IProcessEnvironment>());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => feed.GetLatestAsync(includePrerelease: false, cts.Token));
    }

    private static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> RespondWithFixture()
    {
        byte[] body = LoadFixture();
        return (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") },
            },
        };
    }

    private static byte[] LoadFixture()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FixtureResource)
            ?? throw new InvalidOperationException($"Embedded fixture '{FixtureResource}' not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static GitHubReleaseFeed CreateFeed(out StubHttpMessageHandler handler, Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        handler = new StubHttpMessageHandler(responder);
        return CreateFeed(handler, Substitute.For<IProcessEnvironment>());
    }

    private static GitHubReleaseFeed CreateFeed(StubHttpMessageHandler handler, IProcessEnvironment env)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GitHubReleaseFeed.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.github.com/"),
        });

        return new GitHubReleaseFeed(factory, NullLogger<GitHubReleaseFeed>.Instance, env);
    }
}
