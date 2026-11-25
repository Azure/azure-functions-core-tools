// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Preflights packages and orchestrates managed-provider installation, removal, and package recovery.
/// </summary>
internal class TemplatePackageService(
    TemplateCatalog catalog,
    TemplatePackageValidator validator,
    ITemplaterFactory factory,
    IFileSystem fileSystem,
    IWorkloadTemplateSource workloadTemplates,
    TemplatePackageVerifier verifier)
{
    private readonly TemplateCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly TemplatePackageValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly ITemplaterFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IWorkloadTemplateSource _workloadTemplates = workloadTemplates ?? throw new ArgumentNullException(nameof(workloadTemplates));
    private readonly TemplatePackageVerifier _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));

    /// <summary>
    /// Validates the downloaded archive's identity, package types, host metadata, and template identities before hive mutation, then installs it.
    /// </summary>
    /// <exception cref="TemplatePackageException">Validation, acquisition, mutation, cache verification, or recovery failed.</exception>
    /// <exception cref="ArgumentException">The source is not an HTTP(S) feed URL.</exception>
    /// <exception cref="IOException">Archive access or an engine operation failed.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the template hive was denied.</exception>
    public virtual async Task<bool> InstallAsync(
        string packageId, string? version, string? source, bool prerelease, CancellationToken cancellationToken)
    {
        TemplateCatalog.ValidatePackageId(packageId);
        string resolvedSource = _catalog.GetSource(source).Source;
        NuGetVersion resolvedVersion = await _catalog.ResolveVersionAsync(packageId, version, resolvedSource, prerelease, cancellationToken);
        using Templater? inspection = _fileSystem.DirectoryExists(_factory.HivePath) ? CreateTemplater() : null;
        IManagedTemplatePackage? previous = inspection is null ? null : await FindInstalledAsync(inspection, packageId, cancellationToken);
        if (previous is not null && NuGetVersion.Parse(previous.Version!) == resolvedVersion)
        {
            return false;
        }

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
        TemplatePackageContents contents = _validator.Validate(staged, packageId, resolvedVersion, requireHostMetadata: true, cancellationToken);
        await ValidateDiscoveryAsync(packagePath, Path.Combine(download.Path, "staged-hive"), contents, cancellationToken);
        string? backupPath = null;
        IReadOnlyList<string> previousIdentities = [];
        NuGetVersion? previousVersion = null;
        if (previous is not null && inspection is not null)
        {
            previousVersion = NuGetVersion.Parse(previous.Version!);
            backupPath = Path.Combine(download.Path, "previous.nupkg");
            _fileSystem.CopyFile(previous.MountPointUri, backupPath);
            await using (Stream backup = _fileSystem.OpenReadIfExists(backupPath)
                ?? throw new TemplatePackageException("The previous template package could not be backed up."))
            {
                TemplatePackageContents previousContents = _validator.Validate(backup, packageId, previousVersion, requireHostMetadata: false, cancellationToken);
                previousIdentities = previousContents.Identities;
                await ValidateDiscoveryAsync(backupPath, Path.Combine(download.Path, "backup-hive"), previousContents, cancellationToken);
            }
        }

        inspection?.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
        bool completed = false;
        bool mutationStarted = false;
        try
        {
            using Templater templater = CreateTemplater();
            IManagedTemplatePackage? installed = await FindInstalledAsync(templater, packageId, cancellationToken);
            if (installed is not null)
            {
                mutationStarted = true;
                await RemoveAsync(templater, installed, cancellationToken);
            }

            mutationStarted = true;
            await InstallArchiveAsync(templater, packagePath, packageId, resolvedVersion, contents.Identities, cancellationToken);
            completed = true;
        }
        finally
        {
            if (mutationStarted && !completed)
            {
                // Recovery must finish even when the operation's token was canceled.
                await RecoverAsync(packageId, backupPath, previousVersion, previousIdentities, CancellationToken.None);
            }
        }

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

        using TempDirectory recovery = _fileSystem.CreateTempDirectory();
        string backupPath = Path.Combine(recovery.Path, "previous.nupkg");
        _fileSystem.CopyFile(installed.MountPointUri, backupPath);
        var previousVersion = NuGetVersion.Parse(installed.Version!);
        IReadOnlyList<string> identities;
        await using (Stream backup = _fileSystem.OpenReadIfExists(backupPath)
            ?? throw new TemplatePackageException("The previous template package could not be backed up."))
        {
            TemplatePackageContents contents = _validator.Validate(backup, packageId, previousVersion, requireHostMetadata: false, cancellationToken);
            identities = contents.Identities;
            await ValidateDiscoveryAsync(backupPath, Path.Combine(recovery.Path, "backup-hive"), contents, cancellationToken);
        }

        inspection.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
        bool completed = false;
        bool mutationStarted = false;
        try
        {
            using Templater templater = CreateTemplater();
            IManagedTemplatePackage current = await FindInstalledAsync(templater, packageId, cancellationToken)
                ?? throw new TemplatePackageException($"Template package '{packageId}' is no longer installed.");
            mutationStarted = true;
            await RemoveAsync(templater, current, cancellationToken);
            await templater.RefreshAsync(cancellationToken);
            if (await FindInstalledAsync(templater, packageId, cancellationToken) is not null)
            {
                throw new TemplatePackageException($"Template package '{packageId}' remains installed after removal.");
            }

            _validator.VerifyUninstalled(await templater.GetTemplatesAsync(current, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            completed = true;
        }
        finally
        {
            if (mutationStarted && !completed)
            {
                await RecoverAsync(packageId, backupPath, previousVersion, identities, CancellationToken.None);
            }
        }
    }

    private string HiveParent => Path.GetDirectoryName(_factory.HivePath)!;

    private TemplateEngineContext Context => new(WorkingDirectory.FromExplicit(HiveParent));

    private Templater CreateTemplater() => _factory.Create(Context);

    private async Task ValidateDiscoveryAsync(string path, string settingsLocation, TemplatePackageContents contents, CancellationToken cancellationToken)
    {
        using Templater templater = _factory.CreateIsolated(Context, settingsLocation);
        Scanner scanner = new(templater.Settings);
        using ScanResult result = await scanner.ScanAsync(path, logValidationResults: false, returnInvalidTemplates: true, cancellationToken);
        _validator.VerifyScanned(result.Templates, contents);
    }

    private async Task InstallArchiveAsync(
        Templater templater, string path, string packageId, NuGetVersion version, IReadOnlyList<string> identities, CancellationToken cancellationToken)
    {
        // Let the managed provider finish the archive-and-registration operation before observing cancellation.
        InstallResult result = await templater.InstallAsync(new InstallRequest(path), CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Success)
        {
            throw new TemplatePackageException($"Template package installation failed: {result.ErrorMessage}");
        }

        IManagedTemplatePackage installed = await FindInstalledAsync(templater, packageId, cancellationToken)
            ?? throw new TemplatePackageException("The engine did not retain the installed package.");
        if (NuGetVersion.Parse(installed.Version!) != version)
        {
            throw new TemplatePackageException("The engine did not retain the requested package version.");
        }

        await templater.RefreshAsync(cancellationToken);
        _validator.VerifyInstalled(
            await templater.GetTemplatesAsync(installed, cancellationToken),
            await templater.GetTemplatesAsync(cancellationToken),
            identities,
            templater.Settings);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task RecoverAsync(
        string packageId, string? backupPath, NuGetVersion? previousVersion, IReadOnlyList<string> previousIdentities, CancellationToken recoveryToken)
    {
        try
        {
            using Templater templater = CreateTemplater();
            IManagedTemplatePackage? partial = await FindInstalledAsync(templater, packageId, recoveryToken);
            if (partial is not null)
            {
                await RemoveAsync(templater, partial, recoveryToken);
            }

            if (backupPath is not null && previousVersion is not null)
            {
                await InstallArchiveAsync(templater, backupPath, packageId, previousVersion, previousIdentities, recoveryToken);
            }
            else
            {
                await templater.RefreshAsync(recoveryToken);
                if (partial is not null)
                {
                    _validator.VerifyUninstalled(await templater.GetTemplatesAsync(partial, recoveryToken));
                }
            }
        }
        catch (IOException ex)
        {
            throw RecoveryFailure(packageId, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw RecoveryFailure(packageId, ex);
        }
        catch (TemplatePackageException ex)
        {
            throw RecoveryFailure(packageId, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw RecoveryFailure(packageId, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw RecoveryFailure(packageId, ex);
        }
    }

    private static TemplatePackageException RecoveryFailure(string packageId, Exception exception)
        => new($"Template package '{packageId}' may be missing or partially installed. Recovery failed: {exception.Message} "
            + "Check the package state before retrying; repeating this command may not repair a partial installation.", exception);

    private async Task<IManagedTemplatePackage?> FindInstalledAsync(Templater templater, string id, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await templater.GetManagedPackagesAsync(cancellationToken);
        return packages.SingleOrDefault(p => string.Equals(p.Identifier, id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RemoveAsync(Templater templater, IManagedTemplatePackage package, CancellationToken cancellationToken)
    {
        // Keep provider deletion and registration updates together, then roll back if cancellation was requested.
        UninstallResult result = await templater.UninstallAsync(package, CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Success)
        {
            throw new TemplatePackageException($"Template package uninstallation failed: {result.ErrorMessage}");
        }
    }
}
