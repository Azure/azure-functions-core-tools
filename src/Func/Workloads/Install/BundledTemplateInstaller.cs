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
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal interface IBundledTemplateInstaller
{
    public Task<PreparedBundledTemplates?> PrepareAsync(string directory, BundledTemplateOrigin origin, CancellationToken cancellationToken);
    /// <summary>
    /// Checks every native owner before admitting the workload, retaining native protection through registration and recovery.
    /// </summary>
    /// <exception cref="InvalidWorkloadException">Native ownership conflicts before workload admission.</exception>
    /// <exception cref="BundledTemplateRegistrationException">Registration fails after workload admission and native recovery.</exception>
    /// <exception cref="BundledTemplateRegistrationCanceledException">Cancellation retains the admitted stack after native recovery.</exception>
    public Task<WorkloadInstallResult> CommitAsync(PreparedBundledTemplates prepared,
        Func<CancellationToken, Task<WorkloadInstallResult>> admitWorkload, CancellationToken cancellationToken);
    public Task ValidateInstalledAsync(PreparedBundledTemplates prepared, CancellationToken cancellationToken);
}

internal sealed record BundledTemplateOrigin(string PackageId, string Version, string Sha256, string Source,
    string? LogicalPackageId = null, string? LogicalVersion = null, string? LogicalSource = null);
internal sealed record BundledTemplateOwner(BundledTemplateOrigin Origin, string PackageId, string Version, string Sha256);
internal sealed record BundledTemplateAsset(string Path, string PackageId, string Version, string Sha256, IReadOnlyList<BasicTemplateDefinition> Templates);
internal sealed record BundledTemplatesManifest(int SchemaVersion, IReadOnlyList<BundledTemplateAsset> Assets);

internal sealed class PreparedBundledTemplates(TempDirectory staging, BundledTemplateOrigin origin,
    IReadOnlyList<(string Archive, BasicTemplateArtifact Artifact)> assets) : IDisposable
{
    public BundledTemplateOrigin Origin { get; } = origin;
    public IReadOnlyList<(string Archive, BasicTemplateArtifact Artifact)> Assets { get; } = assets;
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

    public async Task<PreparedBundledTemplates?> PrepareAsync(string directory, BundledTemplateOrigin origin, CancellationToken cancellationToken)
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

        TempDirectory staging = _fileSystem.CreateTempDirectory();
        try
        {
            List<(string Archive, BasicTemplateArtifact Artifact)> assets = [];
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> identities = new(StringComparer.Ordinal);
            foreach (BundledTemplateAsset asset in manifest.Assets)
            {
                if (asset is null || !IsRelativeAsset(asset.Path) || !paths.Add(asset.Path)
                    || string.IsNullOrWhiteSpace(asset.PackageId) || !Regex.IsMatch(asset.PackageId, @"\A[A-Za-z0-9_.-]+\z")
                    || !ids.Add(asset.PackageId) || !NuGetVersion.TryParse(asset.Version, out NuGetVersion? version)
                    || asset.Sha256 is null || !Regex.IsMatch(asset.Sha256, @"\A[0-9a-fA-F]{64}\z")
                    || asset.Templates is null || asset.Templates.Count == 0
                    || asset.Templates.Any(template => template is null || string.IsNullOrWhiteSpace(template.Identity)
                        || !identities.Add(template.Identity) || template.Type is not ("project" or "item")
                        || string.IsNullOrWhiteSpace(template.Path)))
                {
                    throw Invalid("Asset paths, identities, versions, digests and complete template inventories must be valid and unique.");
                }

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
                BasicTemplateArtifact artifact = new(asset.PackageId, version, origin.Source, asset.Sha256, origin.Sha256, asset.Templates);
                await ValidateArtifactAsync(archive, artifact, cancellationToken);

                assets.Add((archive, artifact));
            }

            TemplateEngineContext context = Context(staging.Path);
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
            using (TemplateEngineSession reader = await _store.OpenReadAsync(context, cancellationToken))
            {
                foreach ((_, BasicTemplateArtifact artifact) in assets)
                {
                    await CheckIncumbentAsync(reader, origin, artifact, cancellationToken);
                }
            }

            return new(staging, origin, assets.AsReadOnly());
        }
        catch
        {
            staging.Dispose();
            throw;
        }
    }

    public async Task<WorkloadInstallResult> CommitAsync(PreparedBundledTemplates prepared,
        Func<CancellationToken, Task<WorkloadInstallResult>> admitWorkload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(admitWorkload);
        foreach ((string archive, BasicTemplateArtifact artifact) in prepared.Assets)
        {
            await ValidateArtifactAsync(archive, artifact, cancellationToken);
        }

        WorkloadInstallResult? admitted = null;
        try
        {
            using TemplateStoreWriteSession transaction = await _store.OpenWriteAsync(Context(_paths.Home), cancellationToken);
            await transaction.ExecuteAsync(async (session, token) =>
            {
                List<bool> reused = [];
                foreach ((_, BasicTemplateArtifact artifact) in prepared.Assets)
                {
                    reused.Add(await CheckIncumbentAsync(session, prepared.Origin, artifact, token));
                }

                token.ThrowIfCancellationRequested();
                admitted = await admitWorkload(token)
                    ?? throw new InvalidOperationException("The workload admission callback returned no result.");
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
                    await _fileSystem.WriteAllTextAsync(OwnerPath(artifact.PackageId),
                        JsonSerializer.Serialize(owner, BundledTemplateJsonContext.Default.BundledTemplateOwner), token);
                }
            }, cancellationToken);
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
        using TemplateEngineSession session = await _store.OpenReadAsync(Context(_paths.Home), cancellationToken);
        foreach ((_, BasicTemplateArtifact artifact) in prepared.Assets)
        {
            if (!await CheckIncumbentAsync(session, prepared.Origin, artifact, cancellationToken))
            {
                throw Invalid($"Required package '{artifact.PackageId}' is not registered. Retry the same stack install without check-only mode.");
            }
        }
    }

    private async Task<bool> CheckIncumbentAsync(TemplateEngineSession session, BundledTemplateOrigin origin,
        BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package => string.Equals(package.Identifier, artifact.PackageId, StringComparison.OrdinalIgnoreCase))];
        IReadOnlyList<ITemplateInfo> templates = await session.PackageManager.GetTemplatesAsync(cancellationToken);
        session.CheckStoreDiagnostics();
        if (templates.Any(template => artifact.Templates.Any(expected => expected.Identity == template.Identity)
            && !matches.Any(package => package.MountPointUri == template.MountPointUri)))
        {
            throw Invalid($"Template identity collision for '{artifact.PackageId}'. User-installed content is not replaced.");
        }

        string? receipt = await _fileSystem.ReadAllTextIfExistsAsync(OwnerPath(artifact.PackageId), cancellationToken);
        BundledTemplateOwner? owner;
        try
        {
            owner = receipt is null ? null : JsonSerializer.Deserialize(receipt, BundledTemplateJsonContext.Default.BundledTemplateOwner);
        }
        catch (JsonException exception)
        {
            throw new InvalidWorkloadException($"Bundled ownership for '{artifact.PackageId}' is malformed. Preserve the installation and request explicit repair.", exception);
        }
        BundledTemplateOwner expectedOwner = new(origin, artifact.PackageId, artifact.Version.ToNormalizedString(), artifact.Sha256);
        if (matches.Length == 0)
        {
            if (owner is not null && owner != expectedOwner)
            {
                throw Invalid($"Bundled ownership for '{artifact.PackageId}' conflicts. Automatic upgrades and side-by-side bundles are not supported.");
            }

            return false;
        }

        if (matches.Length != 1 || owner != expectedOwner)
        {
            throw Invalid($"Package '{artifact.PackageId}' has unknown or conflicting native ownership. No replacement is authorized.");
        }

        await ValidateInstalledAsync(session, artifact, cancellationToken);
        return true;
    }

    private async Task ValidateInstalledAsync(TemplateEngineSession session, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package => string.Equals(package.Identifier, artifact.PackageId, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length != 1 || !NuGetVersion.TryParse(matches[0].Version, out NuGetVersion? version) || version != artifact.Version)
        {
            throw Invalid($"The native provider did not register exact package '{artifact.PackageId}' {artifact.Version}.");
        }

        await ValidateArtifactAsync(matches[0].MountPointUri, artifact, cancellationToken);
        IReadOnlyList<ITemplateInfo> templates = await session.PackageManager.GetTemplatesAsync(cancellationToken);
        foreach (ITemplateInfo template in templates.Where(template => template.MountPointUri == matches[0].MountPointUri))
        {
            try
            {
                FuncHostTemplateMetadataReader.Read(template);
            }
            catch (InvalidTemplateMetadataException exception)
            {
                throw new InvalidWorkloadException($"Invalid bundled template host metadata: {exception.Message}", exception);
            }
        }
        string[] actual = [.. templates.Where(template => template.MountPointUri == matches[0].MountPointUri).Select(template => template.Identity)];
        session.CheckStoreDiagnostics();
        if (actual.Length != artifact.Templates.Count || artifact.Templates.Any(template => !actual.Contains(template.Identity, StringComparer.Ordinal)))
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

    private string OwnerPath(string id) => Path.Combine(_paths.Home, "templates", ".bundled-owners", id.ToLowerInvariant() + ".json");

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
