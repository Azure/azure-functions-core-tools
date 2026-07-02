// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    HttpClient httpClient,
    ILogger<CdnReleaseFeed> logger) : IReleaseFeed
{
    internal const string ManifestPath = "public/cli/v5/version.json";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ILogger<CdnReleaseFeed> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Release> GetLatestAsync(bool includePrerelease, CancellationToken cancellationToken)
    {
        VersionManifest manifest = await FetchManifestAsync(cancellationToken);

        if (includePrerelease)
        {
            // Pick whichever is higher: stable or preview.
            SemVersion? stable = TryParseVersion(manifest.Stable, "stable");
            SemVersion? preview = TryParseVersion(manifest.Preview, "preview");

            // PrecedenceComparer handles nulls — null sorts before any version.
            SemVersion? best = SemVersion.PrecedenceComparer.Compare(preview, stable) > 0 ? preview : stable;

            if (best is null)
            {
                throw new InvalidOperationException(
                    $"Error reading version manifest from '{ManifestPath}': no valid version found");
            }

            return new Release(best, BuildDownloadUri(best));
        }
        else
        {
            if (string.IsNullOrEmpty(manifest.Stable))
            {
                throw new InvalidOperationException(
                    $"Error reading version manifest from '{ManifestPath}': version is missing");
            }

            if (!SemVersion.TryParse(manifest.Stable, SemVersionStyles.Strict, out SemVersion? version))
            {
                throw new InvalidOperationException(
                    $"Error reading version manifest from '{ManifestPath}': invalid version '{manifest.Stable}'");
            }

            return new Release(version, BuildDownloadUri(version));
        }
    }

    public async Task<Release> GetVersionAsync(SemVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        Uri downloadUri = BuildDownloadUri(version);

        // Verify the artifact exists on CDN with a HEAD request.
        using var request = new HttpRequestMessage(HttpMethod.Head, downloadUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Version {version} not found on CDN at '{downloadUri}'");
        }

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Error checking version {version} at '{downloadUri}': {response.StatusCode}", ex);
        }

        return new Release(version, downloadUri);
    }

    private async Task<VersionManifest> FetchManifestAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(ManifestPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Error reading version manifest from '{ManifestPath}': {response.StatusCode}");
        }

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
            throw new InvalidOperationException(
                $"Error reading version manifest from '{ManifestPath}': invalid format", ex);
        }

        if (manifest is null)
        {
            throw new InvalidOperationException(
                $"Error reading version manifest from '{ManifestPath}': invalid format");
        }

        return manifest;
    }

    private SemVersion? TryParseVersion(string? versionString, string fieldName)
    {
        if (string.IsNullOrEmpty(versionString))
        {
            return null;
        }

        if (!SemVersion.TryParse(versionString, SemVersionStyles.Strict, out SemVersion? version))
        {
            _logger.LogWarning(
                "CDN manifest contains unparseable version '{Version}' for {Field}.",
                versionString,
                fieldName);
            return null;
        }

        return version;
    }

    private static Uri BuildDownloadUri(SemVersion version)
    {
        string rid = RuntimeInformation.RuntimeIdentifier;
        return new Uri($"public/cli/v5/{version}/Azure.Functions.Cli.{rid}.{version}.zip", UriKind.Relative);
    }
}
