// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine:
//   repo:   https://github.com/dotnet/templating
//   source: src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/Filters/TemplateJsonExistencePackFilter.cs
//           src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/PackChecking/PackSourceChecker.cs (TryGetTemplatesInPackAsync)
//   version: 10.0.302
// See README.md in this directory for the full provenance note and list of adaptations.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Utils;

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// Scans candidate packages with the real template engine. Exposes a cheap <c>template.json</c> presence
/// prefilter so packages that obviously contain no templates are skipped before a full scan.
/// </summary>
internal sealed class PackageScanner(TemplateEngineHostFactory hostFactory)
{
    private readonly TemplateEngineHostFactory _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));

    /// <summary>
    /// Returns <see langword="true"/> when the package contains at least one <c>template.json</c> file.
    /// </summary>
    public bool ContainsTemplateJson(string packagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(packagePath);

        ITemplateEngineHost host = _hostFactory.CreateHost("func-discovery-filter");
        using var environmentSettings = new EngineEnvironmentSettings(host, virtualizeSettings: true);
        foreach (IMountPointFactory factory in environmentSettings.Components.OfType<IMountPointFactory>())
        {
            if (factory.TryMount(environmentSettings, null, packagePath, out IMountPoint? mountPoint))
            {
                bool hasTemplateJson = mountPoint!.Root.EnumerateFiles("template.json", SearchOption.AllDirectories).Any();
                mountPoint.Dispose();
                return hasTemplateJson;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans the package and returns the templates the engine can load. Returns an empty list when the
    /// package cannot be scanned, so a single malformed package never aborts the whole run.
    /// </summary>
    public async Task<IReadOnlyList<ITemplateInfo>> ScanAsync(string packageName, string packagePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageName);
        ArgumentException.ThrowIfNullOrEmpty(packagePath);

        ITemplateEngineHost host = _hostFactory.CreateHost("func-discovery-" + packageName);
        using var environmentSettings = new EngineEnvironmentSettings(host, virtualizeSettings: true);
        var scanner = new Scanner(environmentSettings);
        try
        {
            using ScanResult scanResult = await scanner.ScanAsync(packagePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return scanResult.Templates.Select(t => t.ToITemplateInfo()).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Per-package resilience: a malformed pack must not abort the whole index build (matches upstream).
            Console.WriteLine($"[warn] Failed to scan {packageName}, it will be skipped. Details: {ex.Message}");
            return [];
        }
    }
}
