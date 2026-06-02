// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Logging;
using Semver;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// <see cref="IReleaseFeed"/> backed by the public GitHub releases API. SemVer
/// classification (stable vs prerelease) is driven by the release tag's
/// SemVer prerelease label, not GitHub's <c>prerelease</c> boolean, so that
/// <c>func update</c> agrees with the existing install scripts.
/// </summary>
internal sealed class GitHubReleaseFeed(
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubReleaseFeed> logger,
    IProcessEnvironment processEnvironment) : IReleaseFeed
{
    /// <summary>Named <see cref="HttpClient"/> identifier; registered alongside this service.</summary>
    internal const string HttpClientName = "github";

    private const string ReleasesPath = "repos/Azure/azure-functions-core-tools/releases?per_page=100";
    private const string GitHubTokenEnvVar = "GITHUB_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ILogger<GitHubReleaseFeed> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IProcessEnvironment _processEnvironment = processEnvironment ?? throw new ArgumentNullException(nameof(processEnvironment));

    public async Task<Release?> GetLatestAsync(bool includePrerelease, CancellationToken cancellationToken)
    {
        IReadOnlyList<Release> releases = await FetchReleasesAsync(cancellationToken);

        return releases
            .Where(r => includePrerelease || !r.IsPrerelease)
            .OrderByDescending(r => r.Version, SemVersion.PrecedenceComparer)
            .FirstOrDefault();
    }

    public async Task<Release?> GetVersionAsync(SemVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        IReadOnlyList<Release> releases = await FetchReleasesAsync(cancellationToken);

        return releases.FirstOrDefault(r => SemVersion.PrecedenceComparer.Equals(r.Version, version));
    }

    private async Task<IReadOnlyList<Release>> FetchReleasesAsync(CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesPath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        string? token = _processEnvironment.Get(GitHubTokenEnvVar);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        EnsureSuccessOrThrowGraceful(response);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        GitHubRelease[]? raw;
        try
        {
            raw = await JsonSerializer.DeserializeAsync(
                stream,
                UpdateJsonContext.Default.GitHubReleaseArray,
                cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new GracefulException(
                "Could not parse the GitHub releases response. The API shape may have changed.",
                ex);
        }

        if (raw is null || raw.Length == 0)
        {
            return [];
        }

        var parsed = new List<Release>(raw.Length);
        foreach (GitHubRelease entry in raw)
        {
            if (TryParse(entry, out Release? release))
            {
                parsed.Add(release);
            }
        }

        return parsed;
    }

    private bool TryParse(GitHubRelease entry, out Release release)
    {
        // Strip a single leading 'v' if present, then require strict SemVer 2.0.
        // Tags that don't parse strictly (e.g. legacy "release-2024-01") are
        // skipped rather than throwing; func update simply ignores them.
        string raw = entry.TagName;
        string stripped = raw.StartsWith('v') ? raw[1..] : raw;

        if (!SemVersion.TryParse(stripped, SemVersionStyles.Strict, out SemVersion? version))
        {
            _logger.LogDebug("Skipping release with unparseable tag {Tag}.", raw);
            release = null!;
            return false;
        }

        IReadOnlyList<ReleaseAsset> assets = entry.Assets is { Count: > 0 }
            ? [.. entry.Assets.Select(a => new ReleaseAsset(a.Name, a.DownloadUrl, a.Size, Sha256: null))]
            : [];

        release = new Release(
            Version: version,
            IsPrerelease: version.IsPrerelease,
            TagName: raw,
            Assets: assets);
        return true;
    }

    private static void EnsureSuccessOrThrowGraceful(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response))
        {
            throw new GracefulException(
                "GitHub API rate limit reached. Set the GITHUB_TOKEN environment variable "
                + "to use an authenticated request (raises the limit from 60 to 5000 req/hr).",
                isUserError: true);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new GracefulException(
                "GitHub returned 404 while listing func releases. The releases endpoint may have moved.",
                isUserError: true);
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new GracefulException(
                $"GitHub returned {(int)response.StatusCode} while listing releases. Try again later.",
                isUserError: true);
        }

        throw new GracefulException(
            $"Unexpected response ({(int)response.StatusCode} {response.ReasonPhrase}) from GitHub releases.",
            isUserError: true);
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? values))
        {
            return false;
        }

        string? first = values.FirstOrDefault();
        return first is not null
            && int.TryParse(first, out int remaining)
            && remaining == 0;
    }
}
