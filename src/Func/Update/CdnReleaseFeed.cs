// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Logging;
using Semver;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// <see cref="IReleaseFeed"/> backed by the Azure Functions CDN. Version
/// discovery uses a lightweight manifest at a well-known URL; artifact
/// downloads are constructed from the version and RID. This avoids GitHub API
/// rate limits and authentication requirements.
/// </summary>
internal sealed class CdnReleaseFeed(
    IHttpClientFactory httpClientFactory,
    ILogger<CdnReleaseFeed> logger) : IReleaseFeed
{
    /// <summary>Named <see cref="HttpClient"/> identifier; registered alongside this service.</summary>
    internal const string HttpClientName = "func-cdn";

    internal const string ManifestPath = "public/cli/v5/version.json";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ILogger<CdnReleaseFeed> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Release?> GetLatestAsync(bool includePrerelease, CancellationToken cancellationToken)
    {
        VersionManifest manifest = await FetchManifestAsync(cancellationToken);

        string? versionString = includePrerelease ? manifest.Preview : manifest.Stable;
        if (string.IsNullOrEmpty(versionString))
        {
            return null;
        }

        if (!SemVersion.TryParse(versionString, SemVersionStyles.Strict, out SemVersion? version))
        {
            _logger.LogWarning("CDN manifest contains unparseable version '{Version}' for channel {Channel}.", versionString, includePrerelease ? "preview" : "stable");
            return null;
        }

        return new Release(
            Version: version,
            IsPrerelease: version.IsPrerelease,
            DownloadUrl: BuildDownloadUrl(version));
    }

    public async Task<Release?> GetVersionAsync(SemVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        // Verify the artifact exists on CDN with a HEAD request.
        string downloadUrl = BuildDownloadUrl(version);

        using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl);

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GracefulException(
                $"CDN returned {(int)response.StatusCode} when checking for version {version}. Try again later.",
                isUserError: true);
        }

        return new Release(
            Version: version,
            IsPrerelease: version.IsPrerelease,
            DownloadUrl: downloadUrl);
    }

    private async Task<VersionManifest> FetchManifestAsync(CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.GetAsync(ManifestPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        EnsureSuccessOrThrowGraceful(response);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        VersionManifest? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync(
                stream,
                UpdateJsonContext.Default.VersionManifest,
                cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new GracefulException(
                "Could not parse the CDN version manifest. The format may have changed.",
                ex);
        }

        if (manifest is null)
        {
            throw new GracefulException(
                "The CDN version manifest was empty or null.",
                isUserError: true);
        }

        return manifest;
    }

    private static string BuildDownloadUrl(SemVersion version)
    {
        string rid = RuntimeInformation.RuntimeIdentifier;
        return $"public/{version}/Azure.Functions.Cli.{rid}.{version}.zip";
    }

    private static void EnsureSuccessOrThrowGraceful(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new GracefulException(
                "CDN version manifest not found. The update feed may not be configured yet.",
                isUserError: true);
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new GracefulException(
                $"CDN returned {(int)response.StatusCode} while fetching the version manifest. Try again later.",
                isUserError: true);
        }

        throw new GracefulException(
            $"Unexpected response ({(int)response.StatusCode} {response.ReasonPhrase}) from CDN version manifest.",
            isUserError: true);
    }
}
