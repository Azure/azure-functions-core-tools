// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Searches template packages and resolves install versions through the shared NuGet transport.
/// </summary>
internal class TemplateCatalog(
    IOptions<TemplateCatalogOptions> options, Func<PackageSource, NuGetProtocolSourceClient> clients, TemplateNuGetSourceProvider sources)
{
    internal const string PackageType = "FuncTemplate";

    private readonly TemplateCatalogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly Func<PackageSource, NuGetProtocolSourceClient> _clients = clients ?? throw new ArgumentNullException(nameof(clients));
    private readonly TemplateNuGetSourceProvider _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    /// <summary>
    /// Searches at most twenty template packages on one HTTP(S) V3 source.
    /// </summary>
    /// <exception cref="ArgumentException">The source is not an HTTP(S) feed URI.</exception>
    /// <exception cref="TemplatePackageException">The feed could not be queried or returned malformed package metadata or JSON.</exception>
    public virtual async Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(
        string? query, string? source, bool? prerelease, CancellationToken cancellationToken)
    {
        NuGetProtocolSourceClient client = _clients(GetSource(source));
        IReadOnlyList<CatalogSearchResult> results = await FromSourceAsync(() => client.SearchAsync(
            new CatalogSearchQuery { Filter = query, Take = 20, IncludePrerelease = prerelease ?? _options.IncludePrerelease },
            PackageType, cancellationToken), client.Source.Source, cancellationToken);
        return [.. results.Take(20)];
    }

    /// <summary>
    /// Resolves an exact version, preferring the latest stable release unless prereleases are requested.
    /// </summary>
    /// <exception cref="TemplatePackageException">The package ID, version, or requested combination is invalid or unavailable.</exception>
    public virtual async Task<NuGetVersion> ResolveVersionAsync(
        string packageId, string? version, string? source, bool prerelease, CancellationToken cancellationToken)
    {
        ValidatePackageId(packageId);
        if (version is not null)
        {
            if (prerelease)
            {
                throw new TemplatePackageException("Use either --version for an exact version or --prerelease for the latest prerelease-inclusive version.");
            }

            if (!SemanticVersion.TryParse(version, out _) || !NuGetVersion.TryParse(version, out NuGetVersion? exact))
            {
                throw new TemplatePackageException($"'{version}' is not an exact SemVer version.");
            }

            return exact;
        }

        List<NuGetVersion> versions = [];
        TemplatePackageException? failure = null;
        bool readSource = false;
        foreach (PackageSource feed in _sources.GetSources(packageId, GetAdditionalSource(source)))
        {
            try
            {
                versions.AddRange(await FromSourceAsync(
                    () => _clients(feed).ListVersionsAsync(packageId, cancellationToken), feed.Source, cancellationToken));
                readSource = true;
            }
            catch (TemplatePackageException ex)
            {
                // Like the engine, allow an available source to satisfy a lookup when another feed is unavailable.
                failure ??= ex;
            }
        }

        if (!readSource && failure is not null)
        {
            throw failure;
        }

        return versions.Where(v => prerelease || !v.IsPrerelease).Max()
            ?? throw new TemplatePackageException($"No matching version of '{packageId}' was found. Check NuGet.Config and --source or use --prerelease.");
    }

    /// <summary>
    /// Opens the selected package from an eligible install feed; the caller owns the stream.
    /// </summary>
    /// <exception cref="TemplatePackageException">No eligible feed could supply the package.</exception>
    public virtual async Task<Stream> OpenPackageAsync(
        string packageId, NuGetVersion version, string? source, CancellationToken cancellationToken)
    {
        ValidatePackageId(packageId);
        ArgumentNullException.ThrowIfNull(version);
        TemplatePackageException? failure = null;
        foreach (PackageSource feed in _sources.GetSources(packageId, GetAdditionalSource(source)))
        {
            try
            {
                return await FromSourceAsync(
                    () => _clients(feed).OpenPackageAsync(packageId, version, cancellationToken), feed.Source, cancellationToken);
            }
            catch (WorkloadPackageNotFoundException ex)
            {
                failure ??= new TemplatePackageException($"Template package '{packageId}' {version} was not found on '{feed.Source}'.", ex);
            }
            catch (TemplatePackageException ex)
            {
                // Another eligible feed may supply the same exact version.
                failure ??= ex;
            }
        }

        throw failure ?? new TemplatePackageException(
            $"No eligible NuGet source can supply '{packageId}' {version}. Check NuGet.Config, source mappings, and --source.");
    }

    /// <summary>
    /// Resolves the additional feed, using nuget.org only as the default search feed.
    /// </summary>
    /// <exception cref="ArgumentException">The source is not an HTTP(S) feed URI.</exception>
    public virtual PackageSource GetSource(string? source)
    {
        return GetAdditionalSource(source) ?? new PackageSource(PackageSourceProvider.DefaultSourceUrl);
    }

    /// <summary>
    /// Resolves an explicit or environment-provided additional feed without injecting a default.
    /// </summary>
    /// <exception cref="ArgumentException">The additional feed is not an HTTP(S) URI.</exception>
    public virtual PackageSource? GetAdditionalSource(string? source)
    {
        string? selected = source ?? _options.Source;
        if (string.IsNullOrWhiteSpace(selected) && source is null)
        {
            return null;
        }

        string value = selected!.Trim();
        PackageSource configured = PackageSourceProvider.Build(value, "Template");
        string canonical = new Uri(configured.Source).AbsoluteUri;
        return new PackageSource(canonical, canonical);
    }

    internal static void ValidatePackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) || !NuGet.Packaging.PackageIdValidator.IsValidPackageId(packageId))
        {
            throw new TemplatePackageException("Supply a remote NuGet package ID, not a template name, directory, or archive path.");
        }
    }

    private async Task<T> FromSourceAsync<T>(Func<Task<T>> operation, string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await operation();
        }
        catch (FatalProtocolException ex)
        {
            throw new TemplatePackageException($"Cannot read template feed '{source}'. Check --source, connectivity, and feed credentials.", ex);
        }
        catch (InvalidDataException ex)
        {
            throw new TemplatePackageException($"Template feed '{source}' returned an invalid response. Check --source.", ex);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            throw new TemplatePackageException($"Template feed '{source}' returned malformed JSON. Check --source.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TemplatePackageException($"Cannot reach template feed '{source}'. Check --source and connectivity.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new TemplatePackageException($"Template feed '{source}' does not provide the required V3 service. Check --source.", ex);
        }
    }
}
