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
    public Task<BasicTemplateSelection> SelectOwnedUpdateAsync(BasicCompanionPlan plan, BasicTemplateArtifact after,
        string selectedProjectIdentity, NativeTemplateOwnerIdentity owner, string workloadInventory,
        TemplateEngineContext context, CancellationToken cancellationToken);
    public Task<BasicTemplateSelection> RevalidateOwnedUpdateAsync(BasicCompanionPlan plan, BasicOwnedTemplateUpdate update,
        TemplateEngineContext context, CancellationToken cancellationToken);
    public Task<bool> EnsureAsync(BasicTemplateArtifact artifact, TemplateEngineContext context, bool offline, CancellationToken cancellationToken);
    public Task<PreparedBasicTemplate> PrepareAsync(BasicTemplateSelection selection, TemplateEngineContext context, bool offline, CancellationToken cancellationToken);
    public Task<bool> CommitAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken);
    public Task RevalidatePreparedAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken);
}

internal sealed record BasicTemplateSelection(BasicTemplateArtifact Artifact, bool Installed)
{
    public BasicOwnedTemplateUpdate? Update { get; init; }
}

internal sealed class PreparedBasicTemplate(BasicTemplateArtifact artifact, TempDirectory? staging, string? archive,
    BasicOwnedTemplateUpdate? update = null, bool alreadyInstalled = false) : IDisposable
{
    public BasicTemplateArtifact Artifact { get; } = artifact;
    public string? Archive { get; } = archive;
    public BasicOwnedTemplateUpdate? Update { get; } = update;
    public bool AlreadyInstalled { get; } = alreadyInstalled;
    public void Dispose() => staging?.Dispose();
}

/// <summary>
/// Installs qualified archives through the protected native lifecycle without replacing an incumbent or inferring user ownership.
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
        BasicTemplateArtifact? retained = installed is null
            ? await FindRetainedCandidateAsync(plan.PackageId, plan.Candidates, cancellationToken)
            : null;
        BasicTemplateArtifact selected = installed ?? retained ?? plan.Candidates[0];
        await CheckNativeIdentitiesAsync(session, selected, cancellationToken);
        return new(selected, installed is not null);
    }

    public async Task<BasicTemplateSelection> SelectOwnedUpdateAsync(BasicCompanionPlan plan, BasicTemplateArtifact after,
        string selectedProjectIdentity, NativeTemplateOwnerIdentity owner, string workloadInventory,
        TemplateEngineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedProjectIdentity);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadInventory);
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(plan.PackageId, after.PackageId, StringComparison.OrdinalIgnoreCase)
            || !plan.Candidates.Any(candidate => BasicAcquisitionApproval.Same(candidate, after))
            || !after.ContentRoles.Any(role => role.Identity == selectedProjectIdentity && role.Role == NativeTemplateContentRole.AdditionalProject)
            || _ownership is not INativeTemplateOwnership ownership || _inspector is not IBasicOwnedArchiveInspector archiveInspector)
        {
            throw Conflict(plan.PackageId, "The selected update is not a qualified owned project artifact.");
        }

        using TemplateEngineSession session = await _store.OpenReadAsync(context, cancellationToken);
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package =>
            string.Equals(package.Identifier, plan.PackageId, StringComparison.OrdinalIgnoreCase))];
        session.CheckStoreDiagnostics();
        if (matches.Length == 0)
        {
            if (await ownership.GetRetainedProofAsync(plan.PackageId, cancellationToken) is not null)
            {
                throw Conflict(plan.PackageId, "A retained owner receipt has no actual installed package. Use its owning repair path.");
            }
            await CheckNativeIdentitiesAsync(session, after, cancellationToken);
            return new(after, false);
        }
        if (matches.Length != 1)
        {
            throw Conflict(plan.PackageId, "The installed package identity is ambiguous.");
        }

        IManagedTemplatePackage installed = matches[0];
        if (NuGetVersion.TryParse(installed.Version, out NuGetVersion? currentVersion) && currentVersion == after.Version)
        {
            BasicTemplateArtifact alreadySelected = await FindReusableAsync(session, plan.PackageId, [after], cancellationToken)
                ?? throw Conflict(plan.PackageId, "The installed candidate is not a qualified reusable package.");
            return new(alreadySelected, true);
        }
        if (installed.Version is not { } installedVersion || !_fileSystem.FileExists(installed.MountPointUri)
            || !_store.IsPathInHive(installed.MountPointUri))
        {
            throw Conflict(plan.PackageId, "The installed package is not a unique archive inside the CLI-owned template hive.");
        }

        string digest = await _fileSystem.ComputeSha256Async(installed.MountPointUri, cancellationToken);
        NativeTemplateOwnerProof receiptProof = await ownership.GetRetainedProofAsync(plan.PackageId, cancellationToken)
            ?? throw new BasicAcquisitionException(BasicAcquisitionFailure.OwnershipUnavailable,
                $"Source/pin ownership for installed '{plan.PackageId}' is unavailable. No update is authorized.");
        NativeTemplateOwnerReceipt receipt = receiptProof.Receipt;
        if (!NuGetVersion.TryParse(installedVersion, out NuGetVersion? oldVersion)
            || receipt.Version != oldVersion.ToNormalizedString()
            || !string.Equals(receipt.Sha256, digest, StringComparison.OrdinalIgnoreCase)
            || receipt.PinnedVersion != receipt.Version
            || !string.Equals(receipt.Source, BasicCompanionManifestReader.Source(after.Source), StringComparison.Ordinal))
        {
            throw Conflict(plan.PackageId, "The actual installed version, source, pin or bytes do not prove one owned update incumbent.");
        }

        if (NuGet.Versioning.VersionComparer.VersionRelease.Compare(after.Version, oldVersion) <= 0)
        {
            throw Conflict(plan.PackageId, "An owned update requires a strictly newer NuGet release version.");
        }

        BasicTemplateArchiveInventory archive = await archiveInspector.InspectOwnedArchiveAsync(installed.MountPointUri, receipt, cancellationToken);
        if (!archive.Templates.Any(template => template.Identity == selectedProjectIdentity && template.Type == "project"))
        {
            throw Conflict(plan.PackageId, "The exact selected project identity is absent from the owned incumbent inventory.");
        }

        BasicTemplateOwnedState before = new(receipt, receiptProof.RawSha256,
            Path.GetFullPath(installed.MountPointUri), archive, owner, workloadInventory, selectedProjectIdentity);
        return new(after, false) { Update = new(before, after) };
    }

    public async Task<BasicTemplateSelection> RevalidateOwnedUpdateAsync(BasicCompanionPlan plan,
        BasicOwnedTemplateUpdate update, TemplateEngineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(plan.PackageId, update.After.PackageId, StringComparison.OrdinalIgnoreCase)
            || !plan.Candidates.Any(candidate => BasicAcquisitionApproval.Same(candidate, update.After)))
        {
            throw Conflict(plan.PackageId, "The approved owned update no longer matches the current companion plan.");
        }

        using TemplateEngineSession session = await _store.OpenReadAsync(context, cancellationToken);
        UpdateAdmission state = await ClassifyUpdateAsync(session, update, cancellationToken);
        return new(update.After, state == UpdateAdmission.After) { Update = update };
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
        if (artifact.ContentRoles.Count > 0 && _ownership is not INativeTemplateOwnership)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.OwnershipUnavailable,
                "Required external native content needs lifecycle-owned durable provenance. No package was acquired or installed.");
        }
        using (TemplateEngineSession reader = await _store.OpenReadAsync(context, cancellationToken))
        {
            await CheckNativeIdentitiesAsync(reader, artifact, cancellationToken);
            if (selection.Update is not null)
            {
                if (selection.Installed)
                {
                    return new(artifact, null, null, selection.Update, alreadyInstalled: true);
                }
            }
            else if (await FindReusableAsync(reader, artifact.PackageId, [artifact], cancellationToken) is not null)
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

            return new(artifact, staging, archive, selection.Update, selection.Installed);
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
            if (prepared.Update is { } update)
            {
                UpdateAdmission admission = await ClassifyUpdateAsync(reader, update, cancellationToken);
                if (prepared.AlreadyInstalled && admission != UpdateAdmission.After)
                {
                    throw Conflict(artifact.PackageId, "The already-installed approved AFTER state changed. Re-run explicit selection and consent.");
                }
                return;
            }
            if (await FindReusableAsync(reader, artifact.PackageId, [artifact], cancellationToken) is null)
            {
                throw Conflict(artifact.PackageId, "Installed content changed after preparation. Re-run explicit repair.");
            }

            return;
        }

        await ValidatePreparedArchiveAsync(prepared, cancellationToken);
    }

    public async Task<bool> CommitAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
    {
        await RevalidatePreparedAsync(prepared, context, cancellationToken);
        BasicTemplateArtifact artifact = prepared.Artifact;
        if (prepared.Update is { } update)
        {
            using TemplateStoreWriteSession updateTransaction = await _store.OpenWriteAsync(context, cancellationToken,
                requireCleanStore: true);
            await updateTransaction.ExecuteAsync(async (session, token) =>
            {
                if (await ClassifyUpdateAsync(session, update, token) != UpdateAdmission.Before)
                {
                    throw Conflict(artifact.PackageId, "The approved BEFORE state changed at the transaction boundary.");
                }
                await CheckNativeIdentitiesAsync(session, artifact, token);
                IManagedTemplatePackage[] installed = [.. (await session.PackageManager.GetManagedTemplatePackagesAsync(false, token))
                    .Where(package => string.Equals(package.Identifier, artifact.PackageId, StringComparison.OrdinalIgnoreCase))];
                if (installed.Length != 1)
                {
                    throw Conflict(artifact.PackageId, "The owned update incumbent changed after admission.");
                }

                IManagedTemplatePackageProvider provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
                IReadOnlyList<UninstallResult> uninstalled = await provider.UninstallAsync(installed, token);
                if (uninstalled.Count != 1 || !uninstalled[0].Success)
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed,
                        $"Owned template update could not remove the exact incumbent '{artifact.PackageId}': {string.Join("; ", uninstalled.Select(result => result.ErrorMessage))}");
                }
                await InstallAsync(session, prepared.Archive
                    ?? throw Conflict(artifact.PackageId, "The approved update archive is missing."), token);
            }, async (session, token) =>
            {
                await ValidateInstalledAsync(session, artifact, token);
                if (_ownership is not INativeTemplateOwnership recorder)
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.OwnershipUnavailable,
                        "Native provenance ownership changed before the approved update could commit.");
                }
                NativeTemplateOwnerProof expected = recorder.CreateProof(artifact);
                await recorder.TransitionAsync(new(update.Before.Receipt, update.Before.ReceiptSha256), artifact, token);
                NativeTemplateOwnerProof? actual = await recorder.GetRetainedProofAsync(artifact.PackageId, token);
                if (actual is null || actual.Receipt != expected.Receipt
                    || !string.Equals(actual.RawSha256, expected.RawSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw Conflict(artifact.PackageId, "The complete approved AFTER receipt was not persisted.");
                }
            }, cancellationToken, async (admission, token) =>
            {
                UpdateAdmission state = await ClassifyUpdateAsync(admission, update, token);
                if (state == UpdateAdmission.After)
                {
                    return false;
                }
                if (prepared.AlreadyInstalled)
                {
                    throw Conflict(artifact.PackageId, "The approved AFTER state reverted to BEFORE. Re-run explicit selection and consent.");
                }
                await ValidatePreparedArchiveAsync(prepared, token);
                await CheckNativeIdentitiesAsync(admission, artifact, token);
                return true;
            });
            return updateTransaction.State == TemplateStoreTransactionState.NoOp;
        }

        if (prepared.Archive is null)
        {
            return true;
        }

        using TemplateStoreWriteSession transaction = await _store.OpenWriteAsync(context, cancellationToken);
        bool reused = false;
        await transaction.ExecuteAsync(async (session, token) =>
        {
            await CheckNativeIdentitiesAsync(session, artifact, token);
            reused = await FindReusableAsync(session, artifact.PackageId, [artifact], token) is not null;
            if (!reused)
            {
                await InstallAsync(session, prepared.Archive, token);
            }
        }, async (session, token) =>
        {
            await ValidateInstalledAsync(session, artifact, token);
            if (!reused && artifact.ContentRoles.Count > 0)
            {
                if (_ownership is not INativeTemplateOwnership recorder)
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.OwnershipUnavailable,
                        "Native provenance ownership changed after preparation. No unowned registration can be committed.");
                }
                await recorder.RecordAsync(artifact, token);
            }
        }, cancellationToken);
        return reused;
    }

    private async Task ValidatePreparedArchiveAsync(PreparedBasicTemplate prepared, CancellationToken cancellationToken)
    {
        if (prepared.Archive is null || !_fileSystem.FileExists(prepared.Archive)
            || !string.Equals(await _fileSystem.ComputeSha256Async(prepared.Archive, cancellationToken),
                prepared.Artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                $"Prepared companion '{prepared.Artifact.PackageId}' content changed. Re-run explicit repair.");
        }
    }

    private async Task<BasicTemplateArtifact?> FindReusableAsync(TemplateEngineSession session, string id,
        IReadOnlyList<BasicTemplateArtifact> candidates, CancellationToken cancellationToken)
    {
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package => string.Equals(package.Identifier, id, StringComparison.OrdinalIgnoreCase))];
        session.CheckStoreDiagnostics();
        if (matches.Length == 0)
        {
            await FindRetainedCandidateAsync(id, candidates, cancellationToken);
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

    private async Task<BasicTemplateArtifact?> FindRetainedCandidateAsync(string id, IReadOnlyList<BasicTemplateArtifact> candidates,
        CancellationToken cancellationToken)
    {
        if (!candidates.Any(candidate => candidate.ContentRoles.Count > 0) || _ownership is not INativeTemplateOwnership ownership)
        {
            return null;
        }
        NativeTemplateOwnerReceipt? receipt = await ownership.GetRetainedAsync(id, cancellationToken);
        if (receipt is null)
        {
            return null;
        }
        BasicTemplateArtifact? candidate = candidates.SingleOrDefault(artifact =>
            artifact.Version.ToNormalizedString() == receipt.Version && artifact.Source == receipt.Source
            && string.Equals(artifact.Sha256, receipt.Sha256, StringComparison.OrdinalIgnoreCase));
        return candidate ?? throw Conflict(id,
            "Retained lifecycle provenance pins different bytes, version or source. No automatic replacement is authorized, even while its package is absent.");
    }

    private async Task<UpdateAdmission> ClassifyUpdateAsync(TemplateEngineSession session, BasicOwnedTemplateUpdate update,
        CancellationToken cancellationToken)
    {
        if (_ownership is not INativeTemplateOwnership ownership || _inspector is not IBasicOwnedArchiveInspector archiveInspector)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.OwnershipUnavailable,
                "Owned template update proof is unavailable. No package was changed.");
        }

        BasicTemplateOwnedState expectedBefore = update.Before;
        BasicTemplateArtifact after = update.After;
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] matches = [.. packages.Where(package =>
            string.Equals(package.Identifier, after.PackageId, StringComparison.OrdinalIgnoreCase))];
        session.CheckStoreDiagnostics();
        if (matches.Length != 1 || matches[0].Version is not { } installedVersion
            || !_fileSystem.FileExists(matches[0].MountPointUri) || !_store.IsPathInHive(matches[0].MountPointUri))
        {
            throw Conflict(after.PackageId, "The update incumbent is missing, ambiguous or outside the snapshotted template hive.");
        }

        NativeTemplateOwnerProof? receiptProof = await ownership.GetRetainedProofAsync(after.PackageId, cancellationToken);
        if (receiptProof is null
            || !NuGetVersion.TryParse(installedVersion, out NuGetVersion? actualVersion)
            || receiptProof.Receipt.Version != actualVersion.ToNormalizedString()
            || !string.Equals(receiptProof.Receipt.Sha256,
                await _fileSystem.ComputeSha256Async(matches[0].MountPointUri, cancellationToken), StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(after.PackageId, "The installed archive and strict retained receipt no longer agree.");
        }

        BasicTemplateArchiveInventory archive = await archiveInspector.InspectOwnedArchiveAsync(matches[0].MountPointUri,
            receiptProof.Receipt, cancellationToken);
        BasicTemplateOwnedState actual = new(receiptProof.Receipt, receiptProof.RawSha256,
            Path.GetFullPath(matches[0].MountPointUri), archive, expectedBefore.Owner, expectedBefore.WorkloadInventory,
            expectedBefore.SelectedProjectIdentity);
        if (SameOwnedState(actual, expectedBefore))
        {
            return UpdateAdmission.Before;
        }

        NativeTemplateOwnerProof expectedAfter = ownership.CreateProof(after);
        if (matches[0].Version == after.Version.ToNormalizedString()
            && actual.Receipt == expectedAfter.Receipt
            && string.Equals(actual.ReceiptSha256, expectedAfter.RawSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actual.Archive.PackageId, after.PackageId, StringComparison.OrdinalIgnoreCase)
            && actual.Archive.Version == after.Version.ToNormalizedString())
        {
            await ValidateInstalledAsync(session, after, cancellationToken);
            return UpdateAdmission.After;
        }

        throw Conflict(after.PackageId, "The live owned package is neither the exact approved BEFORE nor the exact approved AFTER state.");
    }

    private static bool SameOwnedState(BasicTemplateOwnedState actual, BasicTemplateOwnedState expected)
        => actual.Receipt == expected.Receipt
            && string.Equals(actual.ReceiptSha256, expected.ReceiptSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actual.ArchivePath, expected.ArchivePath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && SameArchiveInventory(actual.Archive, expected.Archive)
            && actual.Owner == expected.Owner
            && actual.WorkloadInventory == expected.WorkloadInventory
            && actual.SelectedProjectIdentity == expected.SelectedProjectIdentity;

    private static bool SameArchiveInventory(BasicTemplateArchiveInventory actual, BasicTemplateArchiveInventory expected)
        => string.Equals(actual.PackageId, expected.PackageId, StringComparison.OrdinalIgnoreCase)
            && actual.Version == expected.Version
            && actual.PackageTypes.SequenceEqual(expected.PackageTypes, StringComparer.OrdinalIgnoreCase)
            && actual.Files.SequenceEqual(expected.Files, StringComparer.Ordinal)
            && actual.Templates.SequenceEqual(expected.Templates);

    private async Task CheckNativeIdentitiesAsync(TemplateEngineSession session, BasicTemplateArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (artifact.ContentRoles.Count == 0)
        {
            return;
        }
        IReadOnlyList<IManagedTemplatePackage> packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, cancellationToken);
        IManagedTemplatePackage[] owned = [.. packages.Where(package =>
            string.Equals(package.Identifier, artifact.PackageId, StringComparison.OrdinalIgnoreCase))];
        IReadOnlyList<ITemplateInfo> templates = await session.PackageManager.GetTemplatesAsync(cancellationToken);
        session.CheckStoreDiagnostics();
        if (templates.Any(template => artifact.Templates.Any(expected => expected.Identity == template.Identity)
            && !owned.Any(package => package.MountPointUri == template.MountPointUri)))
        {
            throw Conflict(artifact.PackageId, "Another native package owns a required template identity. No replacement or adoption is authorized.");
        }
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
        if (artifact.ContentRoles.Count > 0)
        {
            try
            {
                foreach (ITemplateInfo template in templates.Where(template => template.MountPointUri == matches[0].MountPointUri))
                {
                    FuncHostTemplateMetadataReader.Read(template);
                }
            }
            catch (InvalidTemplateMetadataException exception)
            {
                throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidArtifact,
                    $"The qualified native host metadata for '{artifact.PackageId}' is invalid: {exception.Message}", exception);
            }
        }
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

    private enum UpdateAdmission
    {
        Before,
        After,
    }

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
