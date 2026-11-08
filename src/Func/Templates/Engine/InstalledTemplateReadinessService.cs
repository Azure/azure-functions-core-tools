// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class InstalledTemplateReadinessService(
    IWorkloadLoader workloadLoader,
    CliConfigurationPathsOptions configurationPaths) : IWorkloadTemplateReadiness
{
    private readonly IWorkloadLoader _workloadLoader = workloadLoader ?? throw new ArgumentNullException(nameof(workloadLoader));
    private readonly CliConfigurationPathsOptions _configurationPaths = configurationPaths
        ?? throw new ArgumentNullException(nameof(configurationPaths));

    public async Task EnsureReadyAsync(WorkloadEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != WorkloadKind.Workload)
        {
            return;
        }

        RuntimeWorkloadInfo? workload = null;
        try
        {
            IReadOnlyList<RuntimeWorkloadInfo> loaded = _workloadLoader.Load([entry]);
            workload = loaded.Single();

            IReadOnlyList<WorkloadTemplatePackage> requiredPackages = workload.Instance.RequiredTemplatePackages;
            if (requiredPackages is null)
            {
                throw new InvalidWorkloadException(
                    $"Workload '{entry.PackageId}' declares a null required template package set.");
            }

            if (requiredPackages.Count == 0)
            {
                return;
            }

            workload.Instance.Configure(new DefaultFunctionsCliBuilder(new ServiceCollection(), workload));
            await RegisterRequiredPackagesAsync(workload.ContentRoot, requiredPackages, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidWorkloadException(
                $"Workload '{entry.PackageId}' {entry.PackageVersion} is installed, but template readiness failed: {exception.Message}",
                exception);
        }
        finally
        {
            workload?.LoadContext.Unload();
        }
    }

    private async Task RegisterRequiredPackagesAsync(
        string contentRoot,
        IReadOnlyList<WorkloadTemplatePackage> requiredPackages,
        CancellationToken cancellationToken)
    {
        if (requiredPackages.Count > TemplaterFactory.MaximumPackageCount)
        {
            throw new InvalidWorkloadException(
                $"The workload requires {requiredPackages.Count} template packages, exceeding the {TemplaterFactory.MaximumPackageCount}-package safety limit.");
        }

        string root = Path.GetFullPath(contentRoot);
        if (!Directory.Exists(root))
        {
            throw new InvalidWorkloadException($"The workload template content directory '{root}' is missing.");
        }

        (string Path, string Id, string Version)[] sourcePackages = [.. requiredPackages.Select(package =>
        {
            if (string.IsNullOrWhiteSpace(package.PackageId)
                || !NuGetVersion.TryParse(package.Version, out NuGetVersion? expectedVersion)
                || string.IsNullOrWhiteSpace(package.RelativePath)
                || Path.IsPathRooted(package.RelativePath))
            {
                throw new InvalidWorkloadException("The workload declares an invalid required template package.");
            }

            string path = Path.GetFullPath(Path.Combine(root, package.RelativePath));
            string relativePath = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relativePath)
                || relativePath == ".."
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidWorkloadException(
                    $"The workload template package path '{package.RelativePath}' is outside its content directory.");
            }

            if (!File.Exists(path))
            {
                throw new InvalidWorkloadException($"The required workload template package '{package.PackageId}' is missing.");
            }

            using FileStream stream = File.OpenRead(path);
            if (stream.Length is <= 0 or > TemplaterFactory.MaximumArchiveBytes)
            {
                throw new InvalidWorkloadException(
                    $"The required workload template package '{package.PackageId}' exceeds the {TemplaterFactory.MaximumArchiveBytes}-byte archive safety limit.");
            }

            using PackageArchiveReader reader = new(stream, leaveStreamOpen: true);
            string actualId = reader.NuspecReader.GetId();
            string actualVersion = reader.NuspecReader.GetVersion().ToNormalizedString();
            string declaredVersion = expectedVersion!.ToNormalizedString();
            if (!string.Equals(actualId, package.PackageId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(actualVersion, declaredVersion, StringComparison.Ordinal))
            {
                throw new InvalidWorkloadException(
                    $"The required workload template package '{package.PackageId}' does not match its declared package identity and version.");
            }

            return (Path: path, Id: package.PackageId, Version: declaredVersion);
        })];
        if (sourcePackages.Select(package => package.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != sourcePackages.Length)
        {
            throw new InvalidWorkloadException("The workload declares duplicate required template package identities.");
        }

        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(root));
        using TemplateEngineSession registration = new(
            context,
            Path.Combine(_configurationPaths.Home, "templates"));
        IReadOnlyList<ITemplatePackage> packages = await registration.PackageManager.GetTemplatePackagesAsync(
            force: false,
            cancellationToken);
        IManagedTemplatePackage[] managedPackages = [.. packages.OfType<IManagedTemplatePackage>()];
        foreach ((string _, string id, string version) in sourcePackages)
        {
            if (managedPackages.Any(package =>
                string.Equals(package.Identifier, id, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(package.Version, version, StringComparison.Ordinal)))
            {
                throw new InvalidWorkloadException(
                    $"The CLI Template Engine hive contains a different version of workload template package '{id}'.");
            }
        }

        string[] missingPaths = [.. sourcePackages
            .Where(source => !managedPackages.Any(package =>
                string.Equals(package.Identifier, source.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(package.Version, source.Version, StringComparison.Ordinal)))
            .Select(source => source.Path)];

        if (missingPaths.Length > 0)
        {
            IManagedTemplatePackageProvider provider = registration.PackageManager
                .GetBuiltInManagedProvider(InstallationScope.Global);
            IReadOnlyList<InstallResult> results = await provider.InstallAsync(
                [.. missingPaths.Select(path => new InstallRequest(path))],
                cancellationToken);
            if (results.Count != missingPaths.Length || results.Any(result => !result.Success))
            {
                throw new InvalidWorkloadException(
                    $"Workload-supplied template registration failed: {string.Join("; ", results.Where(result => !result.Success).Select(result => result.ErrorMessage))}");
            }
        }

        IReadOnlyList<ITemplatePackage> verified = await registration.PackageManager.GetTemplatePackagesAsync(
            force: true,
            cancellationToken);
        IManagedTemplatePackage[] verifiedPackages = [.. verified.OfType<IManagedTemplatePackage>()];
        foreach ((string path, string id, string version) in sourcePackages)
        {
            IManagedTemplatePackage[] matches = [.. verifiedPackages.Where(package =>
                string.Equals(package.Identifier, id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(package.Version, version, StringComparison.Ordinal))];
            if (matches.Length != 1
                || string.IsNullOrWhiteSpace(matches[0].Identifier)
                || string.IsNullOrWhiteSpace(matches[0].Version))
            {
                throw new InvalidWorkloadException(
                    $"The CLI Template Engine hive does not contain exactly one verified registration for workload template package '{path}'. " +
                    $"Registered packages: {string.Join("; ", verifiedPackages.Select(package => $"{package.Identifier}@{package.Version} ({package.MountPointUri})"))}.");
            }
        }

        if (verifiedPackages.Select(package => package.Identifier)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != verifiedPackages.Length)
        {
            throw new InvalidWorkloadException("The CLI Template Engine hive contains duplicate template package identities.");
        }
    }
}
