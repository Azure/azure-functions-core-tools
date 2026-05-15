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
    public PackageSource GetSource(string? source = null)
    {
        if (!string.IsNullOrWhiteSpace(source))
        {
            return Build(source.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_options.Source))
        {
            return Build(_options.Source.Trim());
        }

        return new PackageSource(DefaultSourceUrl, DefaultSourceName);
    }

    private static PackageSource Build(string value)
    {
        // Only http(s) V3 NuGet feeds are supported as workload sources.
        // To install from a local .nupkg, pass the file path positionally to
        // 'func workload install' instead of pointing --source at a folder.
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return new PackageSource(value, value);
        }

        throw new ArgumentException(
            $"Source '{value}' is not a supported NuGet feed. Workload sources must be an absolute http(s) URL pointing at a V3 service index.",
            nameof(value));
    }
}
