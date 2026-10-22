// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Discovery;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal interface IBundledTemplateInstaller
{
    public Task<PreparedBundledTemplates?> PrepareAsync(string directory, BundledTemplateOrigin origin, CancellationToken cancellationToken,
        PreparedBundledTemplates? replacing = null, bool retainPrevious = false, bool checkOnly = false);
    /// <summary>
    /// Reads exact version ownership, including legacy receipts, without migrating or writing native state.
    /// </summary>
    /// <exception cref="InvalidWorkloadException">The package identity or its ownership receipts are invalid or conflicting.</exception>
    public Task<BundledTemplateOwner?> ReadOwnerAsync(string packageId, string version, CancellationToken cancellationToken);
    /// <summary>
    /// Reads the same exact owner together with unchanged receipt bytes for source revalidation.
    /// </summary>
    /// <exception cref="InvalidWorkloadException">The package identity or its ownership receipts are invalid or conflicting.</exception>
    public Task<BundledTemplateOwnerDocument?> ReadOwnerDocumentAsync(string packageId, string version, CancellationToken cancellationToken);
    /// <summary>
    /// Checks every native owner before admitting the workload, retaining native protection through registration and recovery.
    /// Explicit updates may supply workload rollback to settle both stores before releasing native protection.
    /// </summary>
    /// <exception cref="InvalidWorkloadException">Native ownership conflicts before workload admission.</exception>
    /// <exception cref="BundledTemplateRegistrationException">Registration fails after workload admission and native recovery.</exception>
    /// <exception cref="BundledTemplateRegistrationCanceledException">Cancellation retains the admitted stack after native recovery.</exception>
    public Task<WorkloadInstallResult> CommitAsync(PreparedBundledTemplates prepared,
        Func<CancellationToken, Task<WorkloadInstallResult>> admitWorkload, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? rollbackWorkload = null);
    public Task ValidateInstalledAsync(PreparedBundledTemplates prepared, CancellationToken cancellationToken);
}

internal sealed record BundledTemplateOrigin(string PackageId, string Version, string Sha256, string Source,
    string? LogicalPackageId = null, string? LogicalVersion = null, string? LogicalSource = null);
internal sealed record BundledTemplateOwner(BundledTemplateOrigin Origin, string PackageId, string Version, string Sha256);
internal sealed record BundledTemplateOwnerDocument(BundledTemplateOwner Owner, string Provenance);
internal sealed record BundledTemplateAsset(string Path, string PackageId, string Version, string Sha256, IReadOnlyList<BasicTemplateDefinition> Templates);
internal sealed record BundledTemplatesManifest(int SchemaVersion, IReadOnlyList<BundledTemplateAsset> Assets);

internal sealed class PreparedBundledTemplates(TempDirectory staging, BundledTemplateOrigin origin,
    IReadOnlyList<(string Archive, BasicTemplateArtifact Artifact)> assets, PreparedBundledTemplates? replacing = null,
    bool retainPrevious = false) : IDisposable
{
    public BundledTemplateOrigin Origin { get; } = origin;
    public IReadOnlyList<(string Archive, BasicTemplateArtifact Artifact)> Assets { get; } = assets;
    public PreparedBundledTemplates? Replacing { get; } = replacing;
    public bool RetainPrevious { get; } = retainPrevious;
    public void Dispose() => staging.Dispose();
}

[JsonSerializable(typeof(BundledTemplatesManifest))]
[JsonSerializable(typeof(BundledTemplateOrigin))]
[JsonSerializable(typeof(BundledTemplateOwner))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class BundledTemplateJsonContext : JsonSerializerContext;

/// <summary>
/// Registers only explicitly declared local archives, preserving native package ownership and protected recovery.
/// </summary>
internal sealed class BundledTemplateInstaller(ITemplateStore store, IBasicArtifactInspector inspector,
    IFileSystem fileSystem, CliConfigurationPathsOptions paths) : IBundledTemplateInstaller
{
    internal const string ManifestName = "bundled-templates.json";
    internal const string OriginName = ".bundled-template-origin.json";
    private readonly ITemplateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IBasicArtifactInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly CliConfigurationPathsOptions _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<PreparedBundledTemplates?> PrepareAsync(string directory, BundledTemplateOrigin origin, CancellationToken cancellationToken,
        PreparedBundledTemplates? replacing = null, bool retainPrevious = false, bool checkOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(origin);
        if (string.IsNullOrWhiteSpace(origin.PackageId) || !NuGetVersion.TryParse(origin.Version, out _)
            || string.IsNullOrWhiteSpace(origin.Source) || origin.Sha256 is null || !Regex.IsMatch(origin.Sha256, @"\A[0-9a-fA-F]{64}\z"))
        {
            throw Invalid("Validated physical workload origin is required.");
        }
        string? json = await _fileSystem.ReadAllTextIfExistsAsync(Path.Combine(directory, ManifestName), cancellationToken);
        if (json is null)
        {
            return null;
        }

        BundledTemplatesManifest manifest = ReadManifest(json);
        TempDirectory staging = _fileSystem.CreateTempDirectory();
        try
        {
            List<(string Archive, BasicTemplateArtifact Artifact)> assets = [];
            foreach (BundledTemplateAsset asset in manifest.Assets)
            {
                string archive = Path.Combine(staging.Path, $"{assets.Count}.nupkg");
                try
                {
                    _fileSystem.CopyFile(Path.Combine(directory, asset.Path.Replace('/', Path.DirectorySeparatorChar)), archive);
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    throw new InvalidWorkloadException($"Declared bundled archive '{asset.Path}' for '{asset.PackageId}' is missing. " +
                        "Preserve the installation and request explicit repair using its original complete workload artifact.", exception);
                }
                BasicTemplateArtifact artifact = new(asset.PackageId, NuGetVersion.Parse(asset.Version), origin.Source, asset.Sha256,
                    origin.Sha256, asset.Templates);
                await ValidateArtifactAsync(archive, artifact, cancellationToken);

                assets.Add((archive, artifact));
            }

            TemplateEngineContext context = Context(staging.Path) with { RequireCleanStore = checkOnly };
            try
            {
                using TemplateEngineSession preflight = new(context, Path.Combine(staging.Path, "hive"));
                foreach ((string archive, BasicTemplateArtifact artifact) in assets)
                {
                    await InstallAsync(preflight, archive, cancellationToken);
                    await ValidateInstalledAsync(preflight, artifact, cancellationToken);
                }
            }
            catch (InvalidTemplateMetadataException exception)
            {
                throw new InvalidWorkloadException($"Invalid bundled template metadata: {exception.Message}", exception);
            }

            // Check incumbent ownership before stack admission, and again inside the exclusive native transaction.
            using (TemplateEngineSession reader = await OpenReadAsync(context, cancellationToken))
            {
                await ValidateReplacementAsync(reader, replacing, cancellationToken);
                foreach ((_, BasicTemplateArtifact artifact) in assets)
                {
                    await CheckIncumbentAsync(reader, origin, artifact, cancellationToken, retainPrevious ? null : replacing);
                }
            }

            return new(staging, origin, assets.AsReadOnly(), replacing, retainPrevious);
        }
        catch
        {
            staging.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Validates a bundled-template declaration without inspecting files or native state.
    /// </summary>
    /// <exception cref="InvalidWorkloadException">The declaration is invalid or ambiguous.</exception>
    internal static BundledTemplatesManifest ReadManifest(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        BundledTemplatesManifest manifest;
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicates(document.RootElement);
            manifest = JsonSerializer.Deserialize(json, BundledTemplateJsonContext.Default.BundledTemplatesManifest)
                ?? throw Invalid("The manifest must be an object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidWorkloadException($"Invalid {ManifestName}: {exception.Message}", exception);
        }

        if (manifest.SchemaVersion != 1 || manifest.Assets is null || manifest.Assets.Count == 0)
        {
            throw Invalid("A schemaVersion 1 manifest requires at least one asset.");
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (BundledTemplateAsset asset in manifest.Assets)
        {
            if (asset is null || !IsRelativeAsset(asset.Path) || !paths.Add(asset.Path)
                || string.IsNullOrWhiteSpace(asset.PackageId) || !Regex.IsMatch(asset.PackageId, @"\A[A-Za-z0-9_.-]+\z")
                || !ids.Add(asset.PackageId) || !NuGetVersion.TryParse(asset.Version, out _)
                || asset.Sha256 is null || !Regex.IsMatch(asset.Sha256, @"\A[0-9a-fA-F]{64}\z")
                || asset.Templates is null || asset.Templates.Count == 0
                || asset.Templates.Any(template => template is null || string.IsNullOrWhiteSpace(template.Identity)
                    || !identities.Add(template.Identity) || template.Type is not ("project" or "item")
                    || string.IsNullOrWhiteSpace(template.Path)))
            {
                throw Invalid("Asset paths, identities, versions, digests and complete template inventories must be valid and unique.");
            }
        }
        return manifest;
    }

    public async Task<WorkloadInstallResult> CommitAsync(PreparedBundledTemplates prepared,
        Func<CancellationToken, Task<WorkloadInstallResult>> admitWorkload, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? rollbackWorkload = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(admitWorkload);
        if (prepared.Replacing is not null && rollbackWorkload is null)
        {
            throw new ArgumentException("Replacing a bundled stack requires workload rollback.", nameof(rollbackWorkload));
        }
        foreach ((string archive, BasicTemplateArtifact artifact) in prepared.Assets)
        {
            await ValidateArtifactAsync(archive, artifact, cancellationToken);
        }

        WorkloadInstallResult? admitted = null;
        try
        {
            using TemplateStoreWriteSession transaction = await _store.OpenWriteAsync(Context(_paths.Home), cancellationToken);
            bool admissionStarted = false;
            IReadOnlyList<(BundledTemplateOwner Owner, string Archive)> protectedOwners = [];
            try
            {
                await transaction.ExecuteAsync(async (session, token) =>
                {
                    await ValidateReplacementAsync(session, prepared.Replacing, token);
                    List<bool> reused = [];
                    foreach ((_, BasicTemplateArtifact artifact) in prepared.Assets)
                    {
                        reused.Add(await CheckIncumbentAsync(session, prepared.Origin, artifact, token,
                            prepared.RetainPrevious ? null : prepared.Replacing));
                    }

                    protectedOwners = await MigrateOwnersAsync(session, prepared, token);
                    token.ThrowIfCancellationRequested();
                    admissionStarted = true;
                    admitted = await admitWorkload(token)
                        ?? throw new InvalidOperationException("The workload admission callback returned no result.");
                    if (prepared.Replacing is not null && !prepared.RetainPrevious)
                    {
                        await RemoveReplacementAsync(session, prepared.Replacing, token);
                    }
                    for (int index = 0; index < prepared.Assets.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!reused[index])
                        {
                            await InstallAsync(session, prepared.Assets[index].Archive, token);
                        }
                    }
                }, async (session, token) =>
                {
                    foreach ((_, BasicTemplateArtifact artifact) in prepared.Assets)
                    {
                        await ValidateInstalledAsync(session, artifact, token);
                        BundledTemplateOwner owner = new(prepared.Origin, artifact.PackageId, artifact.Version.ToNormalizedString(), artifact.Sha256);
                        await WriteOwnerAsync(owner, token);
                    }
                    await ValidateRetainedOwnersAsync(session, prepared, protectedOwners, token);
                }, cancellationToken);
            }
            catch when (admissionStarted && rollbackWorkload is not null)
            {
                // Recovery and workload rollback both settle before the native lease is released.
                admitted = null;
                await rollbackWorkload(CancellationToken.None);
                throw;
            }
        }
        catch (OperationCanceledException exception) when (admitted is not null)
        {
            throw new BundledTemplateRegistrationCanceledException(admitted.Entry, exception, cancellationToken);
        }
        catch (Exception exception) when (admitted is not null
            && exception is InvalidWorkloadException or BasicAcquisitionException or InvalidTemplateMetadataException or IOException or UnauthorizedAccessException)
        {
            throw new BundledTemplateRegistrationException(admitted.Entry, exception);
        }

        return admitted ?? throw new InvalidOperationException("The protected bundled transaction completed without workload admission.");
    }

    public async Task ValidateInstalledAsync(PreparedBundledTemplates prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        using TemplateEngineSession session = await OpenReadAsync(Context(_paths.Home) with { RequireCleanStore = true }, cancellationToken);
        foreach ((_, BasicTemplateArtifact artifact) in prepared.Assets)
        {
            if (!await CheckIncumbentAsync(session, prepared.Origin, artifact, cancellationToken))
            {
                throw Invalid($"Required package '{artifact.PackageId}' is not registered. Retry the same stack install without check-only mode.");
            }
        }
    }

    private async Task<bool> CheckIncumbentAsync(TemplateEngineSession session, BundledTemplateOrigin origin,
        BasicTemplateArtifact artifact, CancellationToken cancellationToken, PreparedBundledTemplates? replacing = null)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] sameId = [.. packages.Where(package =>
            string.Equals(package.Identifier, artifact.PackageId, StringComparison.OrdinalIgnoreCase))];
        IManagedTemplatePackage[] matches = [.. sameId.Where(package => HasVersion(package, artifact.Version))];
        HashSet<string> otherOwnedVersions = new(StringComparer.Ordinal);
        foreach (IManagedTemplatePackage package in sameId.Where(package => !HasVersion(package, artifact.Version)))
        {
            if (!NuGetVersion.TryParse(package.Version, out NuGetVersion? version)
                || sameId.Count(candidate => HasVersion(candidate, version)) != 1)
            {
                throw Invalid($"Package '{package.Identifier}' {package.Version} has ambiguous native registrations. No replacement is authorized.");
            }
            BundledTemplateOwner owner = await ReadOwnerAsync(package.Identifier, version.ToNormalizedString(), cancellationToken)
                ?? throw Invalid($"Package '{package.Identifier}' {package.Version} has unknown or conflicting native ownership. No replacement is authorized.");
            await ValidateOwnerArchiveAsync(package, owner, cancellationToken);
            otherOwnedVersions.Add(package.MountPointUri);
        }
        IReadOnlyList<ITemplatePackage> allPackages = await session.PackageManager.GetTemplatePackagesAsync(force: false, cancellationToken);
        Scanner scanner = new(session.Settings);
        foreach (ITemplatePackage package in allPackages)
        {
            if (matches.Any(match => match.MountPointUri == package.MountPointUri)
                || otherOwnedVersions.Contains(package.MountPointUri)
                || (package is IManagedTemplatePackage managed && replacing is not null && replacing.Assets.Any(asset =>
                    string.Equals(asset.Artifact.PackageId, managed.Identifier, StringComparison.OrdinalIgnoreCase)
                    && HasVersion(managed, asset.Artifact.Version))))
            {
                continue;
            }

            using ScanResult scanned = await scanner.ScanAsync(package.MountPointUri, logValidationResults: false,
                returnInvalidTemplates: true, cancellationToken);
            if (scanned.Templates.Any(template => artifact.Templates.Any(expected => expected.Identity == template.Identity)))
            {
                throw Invalid($"Template identity collision for '{artifact.PackageId}'. User-installed content is not replaced.");
            }
        }
        session.CheckStoreDiagnostics();

        BundledTemplateOwner? incumbent = await ReadOwnerAsync(artifact.PackageId, artifact.Version.ToNormalizedString(), cancellationToken);
        BundledTemplateOwner expectedOwner = new(origin, artifact.PackageId, artifact.Version.ToNormalizedString(), artifact.Sha256);
        BasicTemplateArtifact? previous = replacing?.Assets.Select(asset => asset.Artifact).SingleOrDefault(candidate =>
            string.Equals(candidate.PackageId, artifact.PackageId, StringComparison.OrdinalIgnoreCase) && candidate.Version == artifact.Version);
        if (previous is not null && incumbent == new BundledTemplateOwner(replacing!.Origin, previous.PackageId,
            previous.Version.ToNormalizedString(), previous.Sha256))
        {
            if (matches.Length != 1)
            {
                throw Invalid($"The incumbent package '{artifact.PackageId}' is not registered. Repair it from its original source before updating.");
            }

            await ValidateInstalledAsync(session, previous, cancellationToken);
            return false;
        }
        if (matches.Length == 0)
        {
            if (incumbent is not null && incumbent != expectedOwner)
            {
                throw Invalid($"Bundled ownership for '{artifact.PackageId}' {artifact.Version} conflicts. No replacement is authorized.");
            }

            return false;
        }

        if (matches.Length != 1 || incumbent != expectedOwner)
        {
            throw Invalid($"Package '{artifact.PackageId}' has unknown or conflicting native ownership. No replacement is authorized.");
        }

        await ValidateInstalledAsync(session, artifact, cancellationToken);
        return true;
    }

    private async Task ValidateReplacementAsync(TemplateEngineSession session, PreparedBundledTemplates? replacing,
        CancellationToken cancellationToken)
    {
        if (replacing is null)
        {
            return;
        }

        foreach ((_, BasicTemplateArtifact artifact) in replacing.Assets)
        {
            if (!await CheckIncumbentAsync(session, replacing.Origin, artifact, cancellationToken))
            {
                throw Invalid($"The incumbent package '{artifact.PackageId}' is not registered. Repair it from its original source before updating.");
            }
        }
    }

    private async Task RemoveReplacementAsync(TemplateEngineSession session, PreparedBundledTemplates replacing,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackageProvider provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
        IManagedTemplatePackage[] owned = [.. packages.Where(package => replacing.Assets.Any(asset =>
            string.Equals(asset.Artifact.PackageId, package.Identifier, StringComparison.OrdinalIgnoreCase)
            && HasVersion(package, asset.Artifact.Version)))];
        IReadOnlyList<UninstallResult> results = await provider.UninstallAsync(owned, cancellationToken);
        if (results.Count != replacing.Assets.Count || results.Any(result => !result.Success))
        {
            throw Invalid("Native bundled-template replacement failed. The previous registration will be restored.");
        }

        foreach ((_, BasicTemplateArtifact artifact) in replacing.Assets)
        {
            BundledTemplateOwner expected = new(replacing.Origin, artifact.PackageId, artifact.Version.ToNormalizedString(), artifact.Sha256);
            await DeleteOwnerAsync(expected, cancellationToken);
        }
    }

    private async Task ValidateInstalledAsync(TemplateEngineSession session, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package =>
            string.Equals(package.Identifier, artifact.PackageId, StringComparison.OrdinalIgnoreCase) && HasVersion(package, artifact.Version))];
        if (matches.Length != 1)
        {
            throw Invalid($"The native provider did not register exact package '{artifact.PackageId}' {artifact.Version}.");
        }

        await ValidateArtifactAsync(matches[0].MountPointUri, artifact, cancellationToken);
        Scanner scanner = new(session.Settings);
        using ScanResult scanned = await scanner.ScanAsync(matches[0].MountPointUri, logValidationResults: false,
            returnInvalidTemplates: true, cancellationToken);
        IReadOnlyList<IScanTemplateInfo> templates = scanned.Templates;
        foreach (IScanTemplateInfo template in templates)
        {
            if (!template.IsValid || template.MountPointUri != scanned.MountPoint.MountPointUri)
            {
                throw Invalid($"The engine did not discover valid exact-package metadata for '{artifact.PackageId}' {artifact.Version}.");
            }
            try
            {
                string? hostPath = template.HostConfigFiles.GetValueOrDefault(FuncTemplateEngineHost.Identifier);
                string? hostData = null;
                if (hostPath is not null)
                {
                    IFile? hostFile = scanned.MountPoint.FileInfo(hostPath);
                    if (hostFile is not { Exists: true })
                    {
                        throw new InvalidTemplateMetadataException("The declared func host metadata could not be read from the exact mounted archive.");
                    }
                    using Stream stream = hostFile.OpenRead();
                    using StreamReader reader = new(stream);
                    hostData = await reader.ReadToEndAsync(cancellationToken);
                }
                FuncHostTemplateMetadataReader.Read(hostPath, hostData);
            }
            catch (InvalidTemplateMetadataException exception)
            {
                throw new InvalidWorkloadException($"Invalid bundled template host metadata: {exception.Message}", exception);
            }
        }
        session.CheckStoreDiagnostics();
        if (templates.Count != artifact.Templates.Count || artifact.Templates.Any(expected => !templates.Any(template =>
            template.Identity == expected.Identity && template.TagsCollection.GetValueOrDefault("type") == expected.Type
            && template.ConfigPlace.Replace('\\', '/').TrimStart('/') == expected.Path)))
        {
            throw Invalid($"The engine did not discover the complete declared inventory for '{artifact.PackageId}'.");
        }
    }

    private async Task ValidateArtifactAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        try
        {
            await _inspector.ValidateAsync(archive, artifact, cancellationToken);
        }
        catch (BasicAcquisitionException exception)
        {
            throw new InvalidWorkloadException(exception.Message, exception);
        }
    }

    private static async Task InstallAsync(TemplateEngineSession session, string archive, CancellationToken cancellationToken)
    {
        IManagedTemplatePackageProvider provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> results = await provider.InstallAsync([new InstallRequest(archive)], cancellationToken);
        if (results.Count != 1 || !results[0].Success)
        {
            throw Invalid($"Native bundled-template registration failed: {string.Join("; ", results.Select(result => result.ErrorMessage))}");
        }
    }

    public async Task<BundledTemplateOwner?> ReadOwnerAsync(string packageId, string version, CancellationToken cancellationToken)
        => (await ReadOwnerDocumentAsync(packageId, version, cancellationToken))?.Owner;

    public async Task<BundledTemplateOwnerDocument?> ReadOwnerDocumentAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (packageId is "." or ".." || !Regex.IsMatch(packageId, @"\A[A-Za-z0-9_.-]+\z"))
        {
            throw Invalid($"Package identity '{packageId}' is invalid. No ownership receipt was read.");
        }
        if (!NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            throw Invalid($"Native package '{packageId}' has no valid exact version. Preserve the installation and request explicit repair.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = parsed.ToNormalizedString();
        BundledTemplateOwnerDocument? owner = await ReadOwnerDocumentFileAsync(OwnerPath(packageId, normalized), packageId, cancellationToken);
        if (owner is not null && !SameVersion(owner.Owner.Version, normalized))
        {
            throw Invalid($"Bundled ownership for '{packageId}' {version} conflicts with its version-qualified receipt.");
        }

        BundledTemplateOwnerDocument? legacy = await ReadOwnerDocumentFileAsync(LegacyOwnerPath(packageId), packageId, cancellationToken);
        if (legacy is not null && SameVersion(legacy.Owner.Version, normalized))
        {
            if (owner is not null && owner.Owner != legacy.Owner)
            {
                throw Invalid($"Bundled ownership for '{packageId}' {version} has conflicting receipts. Preserve the installation and request explicit repair.");
            }
            owner = owner is null ? legacy : owner with { Provenance = owner.Provenance + legacy.Provenance };
        }
        return owner;
    }

    private async Task<BundledTemplateOwner?> ReadOwnerFileAsync(string path, string id, CancellationToken cancellationToken)
        => (await ReadOwnerDocumentFileAsync(path, id, cancellationToken))?.Owner;

    private async Task<BundledTemplateOwnerDocument?> ReadOwnerDocumentFileAsync(string path, string id, CancellationToken cancellationToken)
    {
        string? json = await _fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken);
        if (json is null)
        {
            return null;
        }
        try
        {
            BundledTemplateOwner owner = JsonSerializer.Deserialize(json, BundledTemplateJsonContext.Default.BundledTemplateOwner)
                ?? throw Invalid($"Bundled ownership for '{id}' is invalid.");
            if (!string.Equals(owner.PackageId, id, StringComparison.OrdinalIgnoreCase) || !NuGetVersion.TryParse(owner.Version, out _)
                || owner.Sha256 is null || !Regex.IsMatch(owner.Sha256, @"\A[0-9a-fA-F]{64}\z") || owner.Origin is null
                || string.IsNullOrWhiteSpace(owner.Origin.PackageId) || !NuGetVersion.TryParse(owner.Origin.Version, out _)
                || string.IsNullOrWhiteSpace(owner.Origin.Source) || owner.Origin.Sha256 is null
                || !Regex.IsMatch(owner.Origin.Sha256, @"\A[0-9a-fA-F]{64}\z"))
            {
                throw Invalid($"Bundled ownership for '{id}' is invalid. Preserve the installation and request explicit repair.");
            }
            return new(owner, json);
        }
        catch (JsonException exception)
        {
            throw new InvalidWorkloadException($"Bundled ownership for '{id}' is malformed. Preserve the installation and request explicit repair.", exception);
        }
    }

    private async Task ValidateOwnerArchiveAsync(IManagedTemplatePackage package, BundledTemplateOwner owner, CancellationToken cancellationToken)
    {
        if (!string.Equals(package.Identifier, owner.PackageId, StringComparison.OrdinalIgnoreCase)
            || !SameVersion(package.Version, owner.Version)
            || !string.Equals(await _fileSystem.ComputeSha256Async(package.MountPointUri, cancellationToken), owner.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid($"The registered archive for '{package.Identifier}' {package.Version} conflicts with its validated bundled owner.");
        }
    }

    private async Task<IReadOnlyList<(BundledTemplateOwner Owner, string Archive)>> MigrateOwnersAsync(TemplateEngineSession session,
        PreparedBundledTemplates prepared, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        List<(BundledTemplateOwner Owner, string Archive)> owners = [];
        HashSet<string> affected = new(prepared.Assets.Select(asset => asset.Artifact.PackageId), StringComparer.OrdinalIgnoreCase);
        if (prepared.Replacing is not null)
        {
            affected.UnionWith(prepared.Replacing.Assets.Select(asset => asset.Artifact.PackageId));
        }
        foreach (IManagedTemplatePackage package in packages.Where(package => affected.Contains(package.Identifier)))
        {
            if (!NuGetVersion.TryParse(package.Version, out NuGetVersion? version))
            {
                throw Invalid($"Native package '{package.Identifier}' has no valid exact version. Preserve the installation and request explicit repair.");
            }
            BundledTemplateOwner? owner = await ReadOwnerAsync(package.Identifier, version.ToNormalizedString(), cancellationToken);
            if (owner is not null)
            {
                await ValidateOwnerArchiveAsync(package, owner, cancellationToken);
                owners.Add((owner, package.MountPointUri));
                if (!_fileSystem.FileExists(OwnerPath(owner.PackageId, owner.Version))
                    || await ReadOwnerFileAsync(LegacyOwnerPath(owner.PackageId), owner.PackageId, cancellationToken) == owner)
                {
                    await WriteOwnerAsync(owner, cancellationToken);
                }
            }
        }
        return owners;
    }

    private async Task ValidateRetainedOwnersAsync(TemplateEngineSession session, PreparedBundledTemplates prepared,
        IReadOnlyList<(BundledTemplateOwner Owner, string Archive)> owners, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        foreach ((BundledTemplateOwner owner, string archive) in owners)
        {
            if (!prepared.RetainPrevious && prepared.Replacing is not null && owner.Origin == prepared.Replacing.Origin
                && prepared.Replacing.Assets.Any(asset => string.Equals(asset.Artifact.PackageId, owner.PackageId, StringComparison.OrdinalIgnoreCase)
                    && SameVersion(asset.Artifact.Version.ToNormalizedString(), owner.Version)))
            {
                continue;
            }

            IManagedTemplatePackage[] matches = [.. packages.Where(package =>
                string.Equals(package.Identifier, owner.PackageId, StringComparison.OrdinalIgnoreCase) && SameVersion(package.Version, owner.Version))];
            if (matches.Length != 1 || matches[0].MountPointUri != archive
                || await ReadOwnerAsync(owner.PackageId, owner.Version, cancellationToken) != owner)
            {
                throw Invalid($"The retained registration for '{owner.PackageId}' {owner.Version} changed during the update.");
            }
            await ValidateOwnerArchiveAsync(matches[0], owner, cancellationToken);
        }
    }

    private async Task WriteOwnerAsync(BundledTemplateOwner owner, CancellationToken cancellationToken)
    {
        await _fileSystem.WriteAllTextAsync(OwnerPath(owner.PackageId, owner.Version),
            JsonSerializer.Serialize(owner, BundledTemplateJsonContext.Default.BundledTemplateOwner), cancellationToken);
        BundledTemplateOwner? legacy = await ReadOwnerFileAsync(LegacyOwnerPath(owner.PackageId), owner.PackageId, cancellationToken);
        if (legacy == owner)
        {
            _fileSystem.DeleteFile(LegacyOwnerPath(owner.PackageId));
        }
    }

    private async Task DeleteOwnerAsync(BundledTemplateOwner owner, CancellationToken cancellationToken)
    {
        if (await ReadOwnerAsync(owner.PackageId, owner.Version, cancellationToken) != owner)
        {
            throw Invalid($"Bundled ownership for '{owner.PackageId}' {owner.Version} changed during replacement.");
        }

        _fileSystem.DeleteFile(OwnerPath(owner.PackageId, owner.Version));
        if (await ReadOwnerFileAsync(LegacyOwnerPath(owner.PackageId), owner.PackageId, cancellationToken) == owner)
        {
            _fileSystem.DeleteFile(LegacyOwnerPath(owner.PackageId));
        }
    }

    private string OwnerPath(string id, string version)
        => Path.Combine(_paths.Home, "templates", ".bundled-owners", id.ToLowerInvariant(),
            NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant() + ".json");

    private string LegacyOwnerPath(string id)
        => Path.Combine(_paths.Home, "templates", ".bundled-owners", id.ToLowerInvariant() + ".json");

    private static bool HasVersion(IManagedTemplatePackage package, NuGetVersion version)
        => NuGetVersion.TryParse(package.Version, out NuGetVersion? parsed) && parsed == version;

    private static bool SameVersion(string? left, string? right)
        => NuGetVersion.TryParse(left, out NuGetVersion? first) && NuGetVersion.TryParse(right, out NuGetVersion? second) && first == second;

    private async Task<TemplateEngineSession> OpenReadAsync(TemplateEngineContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await _store.OpenReadAsync(context, cancellationToken);
        }
        catch (TemplateStoreRecoveryRequiredException exception)
        {
            throw new InvalidWorkloadException($"Bundled templates cannot be checked. {exception.Message}", exception);
        }
    }

    private static TemplateEngineContext Context(string directory) => new(WorkingDirectory.FromExplicit(directory));

    private static bool IsRelativeAsset(string? path)
        => path is not null && path.StartsWith("tools/", StringComparison.Ordinal) && path.EndsWith(".nupkg", StringComparison.Ordinal)
            && !path.Contains('\\') && !path.Contains(':') && path.Split('/').All(segment => segment is not ("" or "." or ".."));

    private static InvalidWorkloadException Invalid(string message) => new($"Bundled templates: {message}");

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw Invalid($"Duplicate manifest property '{property.Name}'.");
                }

                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                RejectDuplicates(item);
            }
        }
    }
}
