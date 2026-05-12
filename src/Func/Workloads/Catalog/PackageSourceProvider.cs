// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Default <see cref="IPackageSourceProvider"/>. Precedence: <c>--source</c> override,
/// then <see cref="WorkloadCatalogOptions.Sources"/>, then nuget.org as a fallback.
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
    public IReadOnlyList<PackageSource> GetSources(string? overrideSource = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideSource))
        {
            return [Classify(overrideSource.Trim(), name: overrideSource.Trim())];
        }

        var resolved = new List<PackageSource>();
        foreach (string entry in _options.Sources)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            resolved.Add(Classify(entry.Trim(), name: entry.Trim()));
        }

        if (resolved.Count > 0)
        {
            return resolved;
        }

        return [new PackageSource(DefaultSourceName, new Uri(DefaultSourceUrl), IsLocal: false)];
    }

    private static PackageSource Classify(string value, string name)
    {
        // http/https => remote v3 feed; otherwise must be an existing directory.
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return new PackageSource(name, uri, IsLocal: false);
        }

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

        return new PackageSource(name, new Uri(fullPath, UriKind.Absolute), IsLocal: true);
    }
}
