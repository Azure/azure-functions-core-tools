// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal interface IBasicTemplateLifecycle
{
    public Task<BasicTemplateSelection> SelectAsync(BasicCompanionPlan plan, TemplateEngineContext context, CancellationToken cancellationToken);
    public Task<bool> EnsureAsync(BasicTemplateArtifact artifact, TemplateEngineContext context, bool offline, CancellationToken cancellationToken);
    public Task<PreparedBasicTemplate> PrepareAsync(BasicTemplateSelection selection, TemplateEngineContext context, bool offline, CancellationToken cancellationToken);
    public Task<bool> CommitAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken);
    public Task RevalidatePreparedAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken);
}

internal sealed record BasicTemplateSelection(BasicTemplateArtifact Artifact, bool Installed);

internal sealed class PreparedBasicTemplate(BasicTemplateArtifact artifact, TempDirectory? staging, string? archive) : IDisposable
{
    public BasicTemplateArtifact Artifact { get; } = artifact;
    public string? Archive { get; } = archive;
    public void Dispose() => staging?.Dispose();
}

/// <summary>
/// Provisional basic-only lifecycle adapter. Installs qualified archives but never replaces an incumbent or infers user ownership.
/// </summary>
internal sealed class BasicTemplateLifecycle(ITemplateStore store, IBasicPackagePayloadSource payloadSource,
    IBasicArtifactInspector inspector, IBasicTemplateOwnership ownership, IFileSystem fileSystem) : IBasicTemplateLifecycle
{
    private readonly ITemplateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IBasicPackagePayloadSource _payloadSource = payloadSource ?? throw new ArgumentNullException(nameof(payloadSource));
    private readonly IBasicArtifactInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    private readonly IBasicTemplateOwnership _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<BasicTemplateSelection> SelectAsync(BasicCompanionPlan plan, TemplateEngineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        if (plan.Candidates.Count == 0)
        {
            throw new ArgumentException("A companion plan must contain qualified candidates.", nameof(plan));
        }

        using TemplateEngineSession session = await _store.OpenReadAsync(context, cancellationToken);
        BasicTemplateArtifact? installed = await FindReusableAsync(session, plan.PackageId, plan.Candidates, cancellationToken);
        return new(installed ?? plan.Candidates[0], installed is not null);
    }

    public async Task<bool> EnsureAsync(BasicTemplateArtifact artifact, TemplateEngineContext context, bool offline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(context);
        BasicTemplateSelection selection = await SelectAsync(new(artifact.PackageId, new VersionRange(artifact.Version, true, artifact.Version, true), [artifact]),
            context, cancellationToken);
        using PreparedBasicTemplate prepared = await PrepareAsync(selection, context, offline, cancellationToken);
        return await CommitAsync(prepared, context, cancellationToken);
    }

    public async Task<PreparedBasicTemplate> PrepareAsync(BasicTemplateSelection selection, TemplateEngineContext context,
        bool offline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);
        BasicTemplateArtifact artifact = selection.Artifact;
        using (TemplateEngineSession reader = await _store.OpenReadAsync(context, cancellationToken))
        {
            if (await FindReusableAsync(reader, artifact.PackageId, [artifact], cancellationToken) is not null)
            {
                return new(artifact, null, null);
            }
        }

        TempDirectory staging = _fileSystem.CreateTempDirectory();
        string archive = Path.Combine(staging.Path, $"{artifact.PackageId}.{artifact.Version.ToNormalizedString()}.nupkg");
        try
        {
            await using (Stream payload = await OpenPayloadAsync(artifact, offline, cancellationToken))
            {
                await _fileSystem.SaveStreamToFileAsync(archive, payload, cancellationToken);
            }

            await _inspector.ValidateAsync(archive, artifact, cancellationToken);
            // Preflight scans a private hive. Neither provider success nor an archive digest alone proves engine discovery.
            using (TemplateEngineSession preflight = new(context, Path.Combine(staging.Path, "hive")))
            {
                await InstallAsync(preflight, archive, cancellationToken);
                await ValidateInstalledAsync(preflight, artifact, cancellationToken);
            }

            return new(artifact, staging, archive);
        }
        catch
        {
            staging.Dispose();
            throw;
        }
    }

    public async Task RevalidatePreparedAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(context);
        BasicTemplateArtifact artifact = prepared.Artifact;
        if (prepared.Archive is null)
        {
            using TemplateEngineSession reader = await _store.OpenReadAsync(context, cancellationToken);
            if (await FindReusableAsync(reader, artifact.PackageId, [artifact], cancellationToken) is null)
            {
                throw Conflict(artifact.PackageId, "Installed content changed after preparation. Re-run explicit repair.");
            }

            return;
        }

        if (!_fileSystem.FileExists(prepared.Archive)
            || !string.Equals(await _fileSystem.ComputeSha256Async(prepared.Archive, cancellationToken), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                $"Prepared companion '{artifact.PackageId}' content changed. Re-run explicit repair.");
        }
    }

    public async Task<bool> CommitAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
    {
        await RevalidatePreparedAsync(prepared, context, cancellationToken);
        BasicTemplateArtifact artifact = prepared.Artifact;
        if (prepared.Archive is null)
        {
            return true;
        }

        using TemplateStoreWriteSession transaction = await _store.OpenWriteAsync(context, cancellationToken);
        bool reused = false;
        await transaction.ExecuteAsync(async (session, token) =>
        {
            reused = await FindReusableAsync(session, artifact.PackageId, [artifact], token) is not null;
            if (!reused)
            {
                await InstallAsync(session, prepared.Archive, token);
            }
        }, (session, token) => ValidateInstalledAsync(session, artifact, token), cancellationToken);
        return reused;
    }

    private async Task<BasicTemplateArtifact?> FindReusableAsync(TemplateEngineSession session, string id,
        IReadOnlyList<BasicTemplateArtifact> candidates, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package => string.Equals(package.Identifier, id, StringComparison.OrdinalIgnoreCase))];
        session.CheckStoreDiagnostics();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length != 1)
        {
            throw Conflict(id, "The installed package identity is ambiguous.");
        }

        IManagedTemplatePackage installed = matches[0];
        if (installed.Version is not { } installedVersion || !_fileSystem.FileExists(installed.MountPointUri))
        {
            throw Conflict(id, "The installed package is not a qualified NuGet archive. Use its owning lifecycle to repair it explicitly.");
        }

        string digest = await _fileSystem.ComputeSha256Async(installed.MountPointUri, cancellationToken);
        BasicTemplateOwner owner = await _ownership.GetAsync(id, installedVersion, digest, cancellationToken)
            ?? throw new BasicAcquisitionException(BasicAcquisitionFailure.OwnershipUnavailable,
                $"Source/pin ownership for installed '{id}' is unavailable. The lifecycle owner must supply verified ownership before repair.");
        string source = BasicCompanionManifestReader.Source(owner.Source);
        BasicTemplateArtifact? candidate = candidates.SingleOrDefault(candidate =>
            NuGetVersion.TryParse(installedVersion, out NuGetVersion? version) && version == candidate.Version
            && candidate.Source == source && string.Equals(candidate.Sha256, digest, StringComparison.OrdinalIgnoreCase));
        if (candidate is null || (owner.PinnedVersion is not null && owner.PinnedVersion != installedVersion))
        {
            throw Conflict(id, "The actual installed version, source, pin or content conflicts with the qualified plan. No automatic replacement is authorized.");
        }

        await ValidateInstalledAsync(session, candidate, cancellationToken);
        return candidate;
    }

    private async Task ValidateInstalledAsync(TemplateEngineSession session, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package => string.Equals(package.Identifier, artifact.PackageId, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length != 1 || !NuGetVersion.TryParse(matches[0].Version, out NuGetVersion? version) || version != artifact.Version)
        {
            throw Conflict(artifact.PackageId, "The provider did not register the qualified exact package.");
        }

        await _inspector.ValidateAsync(matches[0].MountPointUri, artifact, cancellationToken);
        IReadOnlyList<ITemplateInfo> templates = await session.PackageManager.GetTemplatesAsync(cancellationToken);
        string[] identities = [.. templates.Where(template => template.MountPointUri == matches[0].MountPointUri).Select(template => template.Identity)];
        session.CheckStoreDiagnostics();
        if (identities.Length != artifact.Templates.Count || artifact.Templates.Any(template => !identities.Contains(template.Identity, StringComparer.Ordinal)))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidArtifact,
                $"The engine did not discover the complete qualified basic inventory for '{artifact.PackageId}'.");
        }
    }

    private static async Task InstallAsync(TemplateEngineSession session, string archive, CancellationToken cancellationToken)
    {
        IManagedTemplatePackageProvider provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> results = await provider.InstallAsync([new InstallRequest(archive)], cancellationToken);
        if (results.Count != 1 || !results[0].Success)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed,
                $"Basic template installation failed: {string.Join("; ", results.Select(result => result.ErrorMessage))}");
        }
    }

    private static BasicAcquisitionException Conflict(string id, string message)
        => new(BasicAcquisitionFailure.InstalledConflict, $"Companion '{id}': {message}");

    private async Task<Stream> OpenPayloadAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken)
    {
        try
        {
            return await _payloadSource.OpenAsync(artifact, offline, cancellationToken);
        }
        catch (Exception exception) when (exception is WorkloadPackageNotFoundException or HttpRequestException or IOException or NuGet.Protocol.Core.Types.FatalProtocolException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.PayloadUnavailable,
                $"Qualified companion '{artifact.PackageId}' {artifact.Version} is unavailable: {exception.Message}", exception);
        }
    }
}
