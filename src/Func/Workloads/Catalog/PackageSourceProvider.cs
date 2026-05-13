// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Options;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Default <see cref="IPackageSourceProvider"/>. Precedence: <c>--source</c> override,
/// then <see cref="WorkloadCatalogOptions.Source"/>, then nuget.org as a fallback.
/// </summary>
internal sealed class PackageSourceProvider(IOptions<WorkloadCatalogOptions> options) : IPackageSourceProvider
{
    /// <summary>
    /// Public nuget.org v3 service index, used when no source is configured.
    /// </summary>
    public const string DefaultSourceUrl = "https://api.nuget.org/v3/index.json";

    private const string DefaultSourceName = "nuget.org";

    private readonly WorkloadCatalogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public PackageSource GetSource(string? overrideSource = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideSource))
        {
            return Build(overrideSource.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_options.Source))
        {
            return Build(_options.Source.Trim());
        }

        return new PackageSource(DefaultSourceUrl, DefaultSourceName);
    }

    private static PackageSource Build(string value)
    {
        // Remote v3 feeds: trust the URL as-is; NuGet validates on first use.
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return new PackageSource(value, value);
        }

        // Local directories: resolve to an absolute path and verify it exists
        // so misconfigurations surface eagerly rather than as "no results".
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(
                $"Source '{value}' is neither an absolute http(s) URL nor a valid local directory path.",
                nameof(value),
                ex);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new ArgumentException(
                $"Local source '{value}' does not exist or is not a directory.",
                nameof(value));
        }

        return new PackageSource(fullPath, value);
    }
}
