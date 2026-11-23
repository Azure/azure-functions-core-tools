// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Resolves packages and orchestrates validated managed installs with hive rollback.
/// </summary>
internal class TemplatePackageService(
    TemplateCatalog catalog,
    TemplatePackageValidator validator,
    ITemplaterFactory factory,
    TemplateHiveTransaction transaction,
    IFileSystem fileSystem,
    IWorkloadTemplateSource workloadTemplates,
    TemplatePackageVerifier verifier)
{
    private readonly TemplateCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly TemplatePackageValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly ITemplaterFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly TemplateHiveTransaction _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IWorkloadTemplateSource _workloadTemplates = workloadTemplates ?? throw new ArgumentNullException(nameof(workloadTemplates));
    private readonly TemplatePackageVerifier _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));

    /// <summary>
    /// Installs a remote NuGet ID and validates its installed archive within a rollback-protected transaction.
    /// </summary>
    /// <exception cref="TemplatePackageException">Validation, acquisition, mutation, cache verification, or recovery failed.</exception>
    /// <exception cref="ArgumentException">The source is not an HTTP(S) feed URL.</exception>
    /// <exception cref="IOException">Installed archive or hive access failed.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the template hive was denied.</exception>
    public virtual async Task<bool> InstallAsync(
        string packageId, string? version, string? source, bool prerelease, CancellationToken cancellationToken)
    {
        TemplateCatalog.ValidatePackageId(packageId);
        string? resolvedSource = _catalog.GetAdditionalSource(source)?.Source;
        NuGetVersion resolvedVersion = await _catalog.ResolveVersionAsync(packageId, version, resolvedSource, prerelease, cancellationToken);
        using Templater inspection = CreateTemplater();
        IManagedTemplatePackage? previous = await FindInstalledAsync(inspection, packageId, cancellationToken);
        if (previous is not null && NuGetVersion.Parse(previous.Version!) == resolvedVersion)
        {
            return false;
        }

        inspection.Dispose();
        using TempDirectory download = _fileSystem.CreateTempDirectory();
        string packagePath = Path.Combine(download.Path, "package.nupkg");
        await using (Stream package = await _catalog.OpenPackageAsync(packageId, resolvedVersion, resolvedSource, cancellationToken))
        {
            await _fileSystem.SaveStreamToFileAsync(packagePath, package, cancellationToken);
        }

        // Verify the staged file itself and hold a read-only handle so the engine copies the verified bytes.
        await using Stream staged = _fileSystem.OpenReadIfExists(packagePath)
            ?? throw new TemplatePackageException("The downloaded template package could not be staged.");
        await _verifier.VerifyAsync(staged, cancellationToken);

        await _transaction.ExecuteAsync(Context, async (templater, token) =>
        {
            IManagedTemplatePackage? installed = await FindInstalledAsync(templater, packageId, token);
            if (installed is not null)
            {
                await RemoveAsync(templater, installed, token);
            }

            // The NuGet installer copies local archives into the hive; folder installs instead retain their original paths.
            InstallRequest request = new(packagePath);
            InstallResult result = await templater.InstallAsync(request, token);
            if (!result.Success)
            {
                throw new TemplatePackageException($"Template package installation failed: {result.ErrorMessage}");
            }

            IManagedTemplatePackage committed = await FindInstalledAsync(templater, packageId, token)
                ?? throw new TemplatePackageException("The engine did not retain the installed package.");
            using Stream archive = _fileSystem.OpenReadIfExists(committed.MountPointUri)
                ?? throw new TemplatePackageException("The engine did not retain a readable installed NuGet archive.");
            IReadOnlyList<string> identities = _validator.Validate(archive, packageId, resolvedVersion, token);
            _validator.VerifyInstalled(await templater.GetTemplatesAsync(committed, token), identities, templater.Settings);
        }, cancellationToken);
        return true;
    }

    /// <summary>
    /// Removes a managed NuGet package ID without touching read-only workload roots.
    /// </summary>
    /// <exception cref="TemplatePackageException">The ID is not installed, belongs to a workload, or mutation/recovery failed.</exception>
    /// <exception cref="IOException">Hive access failed.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the template hive was denied.</exception>
    public virtual async Task UninstallAsync(string packageId, CancellationToken cancellationToken)
    {
        TemplateCatalog.ValidatePackageId(packageId);
        using Templater inspection = CreateTemplater();
        IManagedTemplatePackage? installed = await FindInstalledAsync(inspection, packageId, cancellationToken);
        if (installed is null)
        {
            IReadOnlyList<WorkloadTemplateRoot> roots = await _workloadTemplates.GetRootsAsync(cancellationToken);
            string message = roots.Any(r => string.Equals(r.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                ? $"'{packageId}' belongs to a workload. Run 'func workload uninstall {packageId}' instead."
                : $"Template package '{packageId}' is not installed. Supply its NuGet package ID, not a template name.";
            throw new TemplatePackageException(message);
        }

        inspection.Dispose();
        await _transaction.ExecuteAsync(Context, async (templater, token) =>
        {
            IManagedTemplatePackage current = await FindInstalledAsync(templater, packageId, token)
                ?? throw new TemplatePackageException($"Template package '{packageId}' is no longer installed.");
            await RemoveAsync(templater, current, token);
            _validator.VerifyUninstalled(await templater.GetTemplatesAsync(current, token));
        }, cancellationToken);
    }

    private string HiveParent => Path.GetDirectoryName(_factory.HivePath)!;

    private TemplateEngineContext Context => new(WorkingDirectory.FromExplicit(HiveParent));

    private Templater CreateTemplater() => _factory.Create(Context);

    private async Task<IManagedTemplatePackage?> FindInstalledAsync(Templater templater, string id, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await templater.GetManagedPackagesAsync(cancellationToken);
        return packages.SingleOrDefault(p => string.Equals(p.Identifier, id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RemoveAsync(Templater templater, IManagedTemplatePackage package, CancellationToken cancellationToken)
    {
        UninstallResult result = await templater.UninstallAsync(package, cancellationToken);
        if (!result.Success)
        {
            throw new TemplatePackageException($"Template package uninstallation failed: {result.ErrorMessage}");
        }
    }
}
