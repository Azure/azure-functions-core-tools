// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.Loader;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Binds policy to an actual loaded runtime and the selected native package's retained provenance.
/// </summary>
internal sealed class NativeTemplatePolicyAdmission(
    IEnumerable<WorkloadNativeTemplatePolicyRegistration> registrations, InstalledProjectStackCatalog stacks,
    IFileSystem files, CliConfigurationPathsOptions paths, INativeTemplateOwnership nativeOwnership,
    ICliVersionProvider hostVersion, IWorkloadStore? workloads = null, IWorkloadPaths? workloadPaths = null,
    IWorkloadDeploymentService? deployment = null, IBasicCompanionCatalog? catalog = null, BasicAcquisitionPlanner? planner = null,
    IBundledTemplateInstaller? bundledTemplates = null)
{
    private readonly IReadOnlyList<WorkloadNativeTemplatePolicyRegistration> _registrations =
        Array.AsReadOnly<WorkloadNativeTemplatePolicyRegistration>([.. registrations ?? throw new ArgumentNullException(nameof(registrations))]);
    private readonly InstalledProjectStackCatalog _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly CliConfigurationPathsOptions _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly INativeTemplateOwnership _nativeOwnership = nativeOwnership ?? throw new ArgumentNullException(nameof(nativeOwnership));
    private readonly ICliVersionProvider _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));
    private readonly IWorkloadStore? _workloads = workloads;
    private readonly IWorkloadPaths? _workloadPaths = workloadPaths;
    private readonly IWorkloadDeploymentService? _deployment = deployment;
    private readonly IBasicCompanionCatalog? _catalog = catalog;
    private readonly BasicAcquisitionPlanner? _planner = planner;
    private readonly IBundledTemplateInstaller? _bundledTemplates = bundledTemplates;

    public bool HasPolicyForStack(string stack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        return _registrations.Any(registration => string.Equals(registration.Policy.Stack, stack, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BoundNativeTemplatePolicy?> TryBindAsync(
        TemplateCatalogEntry entry, string mountPoint, CancellationToken cancellationToken,
        bool required = false, bool allowCoreLayoutFallback = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(mountPoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Package is not { Version: { } version } package || !PackageIdValidator.IsValidPackageId(package.Identifier)
            || !NuGetVersion.TryParse(version, out _))
        {
            return null;
        }

        BundledTemplateOwnerDocument? bundled = await ReadBundledOwnerAsync(package, cancellationToken);
        NativeTemplateOwnerReceipt? external = await _nativeOwnership.GetRetainedAsync(package.Identifier, cancellationToken);
        if (bundled is null && external is null)
        {
            if (_catalog is not null)
            {
                BasicCompanionManifest manifest = await _catalog.LoadAsync(cancellationToken);
                if (manifest.Artifacts.Any(artifact => artifact.PackageId.Equals(package.Identifier, StringComparison.OrdinalIgnoreCase)
                    && artifact.ContentRoles.Any(role => role.Identity == entry.Identity)))
                {
                    throw Invalid("The selected external template has no retained native provenance. Repair it through its owning setup lifecycle.");
                }
            }
            return null;
        }
        if (bundled is not null && external is not null)
        {
            throw Invalid("The selected native package has conflicting bundled and external ownership.");
        }

        List<BoundNativeTemplatePolicy> matches = [];
        foreach (WorkloadNativeTemplatePolicyRegistration registration in _registrations)
        {
            if (!CanBelongToRuntime(registration, bundled)
                || (external is not null && !await HasDeclaredAssociationAsync(registration, entry, external, cancellationToken)))
            {
                continue;
            }
            string fingerprint = await CaptureAsync(registration, entry, mountPoint, cancellationToken);
            matches.Add(new(registration, entry, mountPoint, external is not null || required, fingerprint,
                token => CaptureAsync(registration, entry, mountPoint, token)));
        }
        bool claimedBasic = bundled is not null && _registrations.Any(registration => MatchesRuntimeOrigin(registration, bundled));
        if (matches.Count == 0 && external is null && !claimedBasic && (!required || allowCoreLayoutFallback))
        {
            return null;
        }
        if (matches.Count != 1)
        {
            throw Invalid(matches.Count == 0
                ? "The selected native source has no authorized loaded workload policy. Update its owning workload and Azure Functions CLI."
                : "Multiple loaded workload policies claim the selected source. Resolve the conflicting installation before retrying.");
        }
        return matches[0];
    }

    private async Task<bool> HasDeclaredAssociationAsync(WorkloadNativeTemplatePolicyRegistration registration,
        TemplateCatalogEntry entry, NativeTemplateOwnerReceipt receipt, CancellationToken cancellationToken)
    {
        if (_catalog is null || _workloads is null)
        {
            return false;
        }
        IReadOnlyList<WorkloadEntry> rows = await _workloads.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry? selected = rows.SingleOrDefault(row =>
            row.PackageId.Equals(registration.Owner.PackageId, StringComparison.OrdinalIgnoreCase)
            && row.PackageVersion == registration.Owner.PackageVersion);
        if (selected is null || !NuGetVersion.TryParse(selected.LogicalPackage?.PackageVersion ?? selected.PackageVersion, out NuGetVersion? version))
        {
            return false;
        }
        BasicCompanionManifest manifest = await _catalog.LoadAsync(cancellationToken);
        string id = selected.LogicalPackage?.PackageId ?? selected.PackageId;
        return manifest.SchemaVersion == 2 && manifest.Stacks.Any(mapping =>
            mapping.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase) && mapping.Versions.Satisfies(version)
            && mapping.Companions.Any(companion => companion.PackageId.Equals(receipt.PackageId, StringComparison.OrdinalIgnoreCase)
                && NuGetVersion.TryParse(receipt.Version, out NuGetVersion? contentVersion) && companion.Versions.Satisfies(contentVersion)))
            && manifest.Artifacts.Any(artifact => artifact.PackageId.Equals(receipt.PackageId, StringComparison.OrdinalIgnoreCase)
                && artifact.Version.ToNormalizedString() == receipt.Version && artifact.Sha256.Equals(receipt.Sha256, StringComparison.OrdinalIgnoreCase)
                && artifact.ContentRoles.Any(content => content.Identity == entry.Identity));
    }

    private bool CanBelongToRuntime(WorkloadNativeTemplatePolicyRegistration registration, BundledTemplateOwnerDocument? bundled)
    {
        IProjectStack? stack = _stacks.FindStack(registration.Policy.Stack);
        if (stack is null || !ReferenceEquals(AssemblyLoadContext.GetLoadContext(stack.GetType().Assembly), registration.Owner.LoadContext)
            || !ReferenceEquals(AssemblyLoadContext.GetLoadContext(registration.Policy.GetType().Assembly), registration.Owner.LoadContext))
        {
            return false;
        }
        if (bundled is null)
        {
            return true;
        }
        return MatchesRuntimeOrigin(registration, bundled);
    }

    private static bool MatchesRuntimeOrigin(WorkloadNativeTemplatePolicyRegistration registration, BundledTemplateOwnerDocument bundled)
    {
        BundledTemplateOwner receipt = bundled.Owner;
        if (receipt.Origin is null || string.IsNullOrWhiteSpace(receipt.Origin.PackageId)
            || !NuGetVersion.TryParse(receipt.Origin.Version, out _))
        {
            throw Invalid("The selected native owner receipt has no valid runtime origin.");
        }
        return receipt.Origin.PackageId.Equals(registration.Owner.PackageId, StringComparison.OrdinalIgnoreCase)
            && SameVersion(receipt.Origin.Version, registration.Owner.PackageVersion);
    }

    private async Task<string> CaptureAsync(WorkloadNativeTemplatePolicyRegistration registration, TemplateCatalogEntry entry,
        string mountPoint, CancellationToken cancellationToken)
    {
        if (_workloads is null || _workloadPaths is null || _deployment is null)
        {
            throw Invalid("Native policy admission requires installed workload payload validation. Repair the CLI composition.");
        }
        IReadOnlyList<WorkloadEntry> installed = await _workloads.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry[] rows = [.. installed.Where(row => row.Kind == WorkloadKind.Workload
            && row.PackageId.Equals(registration.Owner.PackageId, StringComparison.OrdinalIgnoreCase)
            && row.PackageVersion == registration.Owner.PackageVersion)];
        if (rows.Length != 1 || !SamePath(_workloadPaths.GetInstallDirectory(rows[0].PackageId, rows[0].PackageVersion),
            registration.Owner.InstallDirectory) || !_files.DirectoryExists(registration.Owner.InstallDirectory)
            || !CanBelongToRuntime(registration, null))
        {
            throw Invalid("The policy no longer belongs to the actual selected installed runtime workload.");
        }
        WorkloadEntry selected = rows[0];
        PreparedWorkloadReuse payload;
        try
        {
            payload = await _deployment.PrepareReuseAsync(selected, cancellationToken, checkOnly: true);
        }
        catch (InvalidWorkloadException exception)
        {
            throw new InvalidDataException("The selected runtime payload is no longer valid. Preserve the installation and repair it.", exception);
        }
        catch (WorkloadPlanChangedException exception)
        {
            throw new InvalidDataException("The actual selected runtime source or ownership changed. Rerun selection.", exception);
        }
        using (payload)
        {
            TemplatePackageInfo package = entry.Package ?? throw Invalid("The selected native package identity is missing.");
            string archive = Uri.TryCreate(mountPoint, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : mountPoint;
            if (!_files.FileExists(archive))
            {
                throw Invalid("The selected source is not its captured native archive.");
            }
            string digest = await _files.ComputeSha256Async(archive, cancellationToken);
            BundledTemplateOwnerDocument? bundled = await ReadBundledOwnerAsync(package, cancellationToken);
            string provenance;
            if (bundled is not null)
            {
                provenance = await ValidateBundledAsync(registration, entry, digest, bundled, cancellationToken);
            }
            else
            {
                provenance = await ValidateExternalAsync(selected, installed, entry, digest, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return JsonSerializer.Serialize(new
            {
                selected,
                registration.Policy.Stack,
                hashes = payload.Hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                provenance,
            });
        }
    }

    private async Task<string> ValidateBundledAsync(WorkloadNativeTemplatePolicyRegistration registration,
        TemplateCatalogEntry entry, string digest, BundledTemplateOwnerDocument document, CancellationToken cancellationToken)
    {
        string root = registration.Owner.InstallDirectory;
        string originJson = await _files.ReadAllTextIfExistsAsync(Path.Combine(root, BundledTemplateInstaller.OriginName), cancellationToken)
            ?? throw Invalid("The runtime bundled-template origin is missing.");
        string manifestJson = await _files.ReadAllTextIfExistsAsync(Path.Combine(root, BundledTemplateInstaller.ManifestName), cancellationToken)
            ?? throw Invalid("The runtime bundled-template declaration is missing.");
        try
        {
            BundledTemplateOrigin origin = JsonSerializer.Deserialize(originJson, BundledTemplateJsonContext.Default.BundledTemplateOrigin)
                ?? throw Invalid("The runtime bundled-template origin is empty.");
            BundledTemplatesManifest manifest = JsonSerializer.Deserialize(manifestJson, BundledTemplateJsonContext.Default.BundledTemplatesManifest)
                ?? throw Invalid("The runtime bundled-template declaration is empty.");
            BundledTemplateOwner receipt = document.Owner;
            TemplatePackageInfo package = entry.Package!;
            if (manifest.SchemaVersion != 1 || manifest.Assets is null
                || origin.PackageId != registration.Owner.PackageId || !SameVersion(origin.Version, registration.Owner.PackageVersion))
            {
                throw Invalid("The runtime bundled-template declaration does not match its actual owner.");
            }
            BundledTemplateAsset[] assets = [.. manifest.Assets.Where(asset => asset is not null && asset.Templates is not null
                && asset.PackageId.Equals(package.Identifier, StringComparison.OrdinalIgnoreCase) && SameVersion(asset.Version, package.Version)
                && asset.Templates.Any(template => template is not null && template.Identity == entry.Identity
                    && template.Type == (entry.Type == TemplateType.Project ? "project" : "item")))];
            if (assets.Length != 1 || receipt != new BundledTemplateOwner(origin, package.Identifier, assets[0].Version, assets[0].Sha256)
                || !digest.Equals(assets[0].Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("The selected native archive does not match its exact bundled asset and owner receipt.");
            }
            string path = assets[0].Path;
            if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(part => part is "" or "." or ".."))
            {
                throw Invalid("The bundled native asset path is invalid.");
            }
            string physical = await _files.ComputeSha256Async(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)), cancellationToken);
            if (!physical.Equals(digest, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("The runtime's retained native archive differs from the selected source.");
            }
            return originJson + manifestJson + document.Provenance;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The bundled template ownership data is malformed.", exception);
        }
    }

    private async Task<string> ValidateExternalAsync(WorkloadEntry selected, IReadOnlyList<WorkloadEntry> installed,
        TemplateCatalogEntry entry, string digest, CancellationToken cancellationToken)
    {
        if (_catalog is null || _planner is null)
        {
            throw Invalid("No qualified external native association is available. Use the owning setup lifecycle.");
        }
        TemplatePackageInfo package = entry.Package!;
        NativeTemplateOwnerReceipt receipt = await _nativeOwnership.GetRetainedAsync(package.Identifier, cancellationToken)
            ?? throw Invalid("The selected external content has no retained native provenance.");
        if (!SameVersion(receipt.Version, package.Version) || !receipt.Sha256.Equals(digest, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("The selected external package differs from its retained identity, version or digest.");
        }
        if (!NuGetVersion.TryParse(_hostVersion.Version, out NuGetVersion? host)
            || !NuGetVersion.TryParse(selected.LogicalPackage?.PackageVersion ?? selected.PackageVersion, out NuGetVersion? version))
        {
            throw Invalid("The actual FUNC host or selected stack version is invalid.");
        }
        NativeTemplateContentRole role = entry.Type == TemplateType.Project ? NativeTemplateContentRole.AdditionalProject : NativeTemplateContentRole.Function;
        BasicCompanionManifest manifest = await _catalog.LoadAsync(cancellationToken);
        BasicAcquisitionRequest request = new(selected.LogicalPackage?.PackageId ?? selected.PackageId, version,
            WorkingDirectory.FromExplicit(_paths.Home), receipt.Source, Check: true, Offline: true, IncludePrerelease: true)
        {
            NativeContext = new(selected, FuncTemplateEngineHost.Identifier, host, null, [role]),
        };
        BasicAcquisitionPlan plan;
        try
        {
            plan = _planner.Plan(manifest, request, installed, receipt.Source)
                ?? throw Invalid("The selected external content has no qualified association.");
        }
        catch (BasicAcquisitionException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
        BasicTemplateArtifact[] matching = [.. plan.Companions.SelectMany(companion => companion.Candidates).Where(artifact =>
            artifact.PackageId.Equals(package.Identifier, StringComparison.OrdinalIgnoreCase)
            && SameVersion(artifact.Version.ToNormalizedString(), package.Version)
            && artifact.Sha256.Equals(digest, StringComparison.OrdinalIgnoreCase) && artifact.ContentRevision == receipt.ContentRevision
            && artifact.ContentRoles.Any(content => content.Identity == entry.Identity && content.Role == role)
            && artifact.Templates.Any(template => template.Identity == entry.Identity
                && template.Type == (entry.Type == TemplateType.Project ? "project" : "item")))];
        if (matching.Length != 1)
        {
            throw Invalid("The actual selected content is not the exact declared compatible artifact and required external role.");
        }
        return JsonSerializer.Serialize(new { receipt, manifest.Revision, artifact = matching[0] });
    }

    private async Task<BundledTemplateOwnerDocument?> ReadBundledOwnerAsync(TemplatePackageInfo package, CancellationToken cancellationToken)
    {
        if (package.Version is not { } version || !NuGetVersion.TryParse(version, out _))
        {
            throw Invalid("The selected native package has no valid exact version. Rerun selection.");
        }
        if (_bundledTemplates is null)
        {
            throw Invalid("Native policy admission requires the bundled ownership reader. Repair the CLI composition.");
        }
        try
        {
            return await _bundledTemplates.ReadOwnerDocumentAsync(package.Identifier, version, cancellationToken);
        }
        catch (InvalidWorkloadException exception)
        {
            throw new InvalidDataException("The selected native bundled ownership is invalid. Preserve the installation and repair it.", exception);
        }
    }

    private static bool SameVersion(string? left, string? right)
        => NuGetVersion.TryParse(left, out NuGetVersion? first) && NuGetVersion.TryParse(right, out NuGetVersion? second) && first == second;

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static InvalidDataException Invalid(string message) => new(message);
}

internal sealed class BoundNativeTemplatePolicy(
    WorkloadNativeTemplatePolicyRegistration registration, TemplateCatalogEntry entry, string mountPoint, bool required,
    string fingerprint, Func<CancellationToken, Task<string>> capture)
{
    private readonly Func<CancellationToken, Task<string>> _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    private readonly string _fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));

    public WorkloadNativeTemplatePolicyRegistration Registration { get; } = registration ?? throw new ArgumentNullException(nameof(registration));
    public TemplateCatalogEntry Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));
    public string MountPoint { get; } = mountPoint ?? throw new ArgumentNullException(nameof(mountPoint));
    public bool Required { get; } = required;

    internal BoundNativeTemplateExecutor BindExecutor(WorkloadNativeTemplatePolicyRegistration owner,
        INativeTemplateEffectCapability capability, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(capability);
        if (!ReferenceEquals(owner, Registration))
        {
            throw new ArgumentException("The executor must belong to this actual runtime-owner admission.", nameof(owner));
        }
        return new(this, capability, confirmed);
    }

    public async Task RevalidateAsync(CancellationToken cancellationToken)
    {
        if (await _capture(cancellationToken) != _fingerprint)
        {
            throw new InvalidDataException("The captured runtime, selected stack or native content provenance changed. Rerun selection.");
        }
    }
}

internal sealed class BoundNativeTemplateExecutor(BoundNativeTemplatePolicy admission, INativeTemplateEffectCapability capability, bool confirmed)
{
    private NativeTemplatePolicySession? _invocation;

    public BoundNativeTemplatePolicy Admission { get; } = admission ?? throw new ArgumentNullException(nameof(admission));
    public INativeTemplateEffectCapability Capability { get; } = capability ?? throw new ArgumentNullException(nameof(capability));
    public bool Confirmed { get; } = confirmed;

    internal void BindInvocation(NativeTemplatePolicySession invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        NativeTemplatePolicySession? previous = Interlocked.CompareExchange(ref _invocation, invocation, null);
        if (previous is not null && !ReferenceEquals(previous, invocation))
        {
            throw new InvalidDataException("This executor admission belongs to another invocation. Compose a fresh owner-bound admission before retrying.");
        }
    }
}
