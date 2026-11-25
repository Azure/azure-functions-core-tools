// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Resolves install versions and downloads template packages through the shared NuGet transport.
/// </summary>
internal class TemplateCatalog(
    IOptions<TemplateCatalogOptions> options, Func<PackageSource, NuGetProtocolSourceClient> clients)
{
    internal const string PackageType = "FuncTemplate";

    private readonly TemplateCatalogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly Func<PackageSource, NuGetProtocolSourceClient> _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    /// <summary>
    /// Resolves an exact version, preferring the latest listed stable release unless prereleases are requested.
    /// </summary>
    /// <exception cref="ArgumentException">The source is not an HTTP(S) feed URI.</exception>
    /// <exception cref="TemplatePackageException">The package ID, version, or requested combination is invalid or unavailable.</exception>
    public virtual async Task<NuGetVersion> ResolveVersionAsync(
        string packageId, string? version, string? source, bool prerelease, CancellationToken cancellationToken)
    {
        ValidatePackageId(packageId);
        cancellationToken.ThrowIfCancellationRequested();
        PackageSource feed = GetSource(source);
        if (version is not null)
        {
            if (prerelease)
            {
                throw new TemplatePackageException("Use either --version for an exact version or --prerelease for the latest prerelease-inclusive version.");
            }

            if (!NuGetVersion.TryParse(version, out NuGetVersion? exact))
            {
                throw new TemplatePackageException($"'{version}' is not an exact NuGet version.");
            }

            return exact;
        }

        IReadOnlyList<NuGetVersion> versions = await FromSourceAsync(
            () => _clients(feed).ListListedVersionsAsync(packageId, cancellationToken), feed.Source, cancellationToken);

        return versions.Where(v => prerelease || !v.IsPrerelease).Max()
            ?? throw new TemplatePackageException($"No matching version of '{packageId}' was found on '{feed.Source}'. Check --source or use --prerelease.");
    }

    /// <summary>
    /// Opens the selected package from the resolved feed; the caller owns the temporary seekable stream.
    /// </summary>
    /// <exception cref="ArgumentException">The source is not an HTTP(S) feed URI.</exception>
    /// <exception cref="TemplatePackageException">The resolved feed could not supply the package.</exception>
    public virtual async Task<Stream> OpenPackageAsync(
        string packageId, NuGetVersion version, string? source, CancellationToken cancellationToken)
    {
        ValidatePackageId(packageId);
        ArgumentNullException.ThrowIfNull(version);
        PackageSource feed = GetSource(source);
        try
        {
            try
            {
                return await FromSourceAsync(
                    () => _clients(feed).OpenPackageAsync(packageId, version, cancellationToken), feed.Source, cancellationToken);
            }
            catch (WorkloadPackageNotFoundException)
            {
                // A stale flat-container index can hide a newly published version.
                return await FromSourceAsync(
                    () => _clients(feed).OpenPackageAsync(packageId, version, cancellationToken, bypassCache: true),
                    feed.Source, cancellationToken);
            }
        }
        catch (WorkloadPackageNotFoundException ex)
        {
            throw new TemplatePackageException(
                $"Template package '{packageId}' {version} was not found on '{feed.Source}'. Check --source and --version.", ex);
        }
    }

    /// <summary>
    /// Resolves the command source, then the template environment source, then nuget.org.
    /// </summary>
    /// <exception cref="ArgumentException">The source is not an HTTP(S) feed URI.</exception>
    public virtual PackageSource GetSource(string? source)
    {
        string? selected = source ?? _options.Source;
        if (string.IsNullOrWhiteSpace(selected) && source is null)
        {
            return new PackageSource(PackageSourceProvider.DefaultSourceUrl);
        }

        return PackageSourceProvider.Build(selected!.Trim(), "Template");
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
        catch (NuGetResourceUnavailableException ex)
        {
            throw new TemplatePackageException($"Template feed '{source}' does not provide the required V3 service. Check --source.", ex);
        }
    }
}
