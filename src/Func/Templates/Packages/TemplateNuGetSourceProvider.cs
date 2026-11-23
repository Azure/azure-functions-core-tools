// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Configuration;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Reads install sources and package source mappings from ambient NuGet configuration.
/// </summary>
internal class TemplateNuGetSourceProvider(DirectoryInfo workingDirectory)
{
    private readonly DirectoryInfo _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));

    public virtual IReadOnlyList<PackageSource> GetSources(string packageId, PackageSource? additionalSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        try
        {
            ISettings settings = GetSettings();
            PackageSource[] configured = [.. new PackageSourceProvider(settings).LoadPackageSources().Where(s => s.IsEnabled)];
            var mapping = PackageSourceMapping.GetPackageSourceMapping(settings);
            IEnumerable<PackageSource> mapped = configured;
            if (mapping.IsEnabled)
            {
                HashSet<string> names = new(mapping.GetConfiguredPackageSources(packageId) ?? [], StringComparer.OrdinalIgnoreCase);
                mapped = configured.Where(s => names.Contains(s.Name));
            }

            IEnumerable<PackageSource> sources = additionalSource is not null
                && !configured.Any(s => string.Equals(s.Source, additionalSource.Source, StringComparison.OrdinalIgnoreCase))
                ? mapped.Prepend(additionalSource) : mapped;
            return [.. sources.Where(s => !s.IsHttp || s.IsHttps)];
        }
        catch (NuGetConfigurationException ex)
        {
            throw new TemplatePackageException("Cannot read NuGet.Config. Check package sources and source mappings.", ex);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new TemplatePackageException("NuGet.Config contains invalid XML. Correct it before retrying.", ex);
        }
        catch (IOException ex)
        {
            throw new TemplatePackageException("Cannot read NuGet.Config. Check filesystem access and retry.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new TemplatePackageException("Cannot read NuGet.Config. Check permissions and retry.", ex);
        }
    }

    internal virtual ISettings GetSettings()
    {
        try
        {
            return Settings.LoadDefaultSettings(_workingDirectory.FullName);
        }
        catch (NuGetConfigurationException ex)
        {
            throw new TemplatePackageException("Cannot read NuGet.Config. Check package sources and trusted signers.", ex);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new TemplatePackageException("NuGet.Config contains invalid XML. Correct it before retrying.", ex);
        }
        catch (IOException ex)
        {
            throw new TemplatePackageException("Cannot read NuGet.Config. Check filesystem access and retry.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new TemplatePackageException("Cannot read NuGet.Config. Check permissions and retry.", ex);
        }
    }
}
