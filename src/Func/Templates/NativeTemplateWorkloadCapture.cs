// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Discovery;
using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Retains exact selected runtime metadata and configuration outcomes during an explicitly captured boot.
/// </summary>
internal sealed class NativeTemplateWorkloadCapture
{
    private readonly List<NativeTemplateWorkloadRegistration> _registrations = [];
    private readonly Dictionary<ServiceDescriptor, bool> _configuredStacks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ServiceDescriptor, RuntimeWorkloadInfo?> _descriptorOwners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IProjectStack, RuntimeWorkloadInfo?> _stackOwners = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<NativeTemplateWorkloadRegistration>? _completed;
    private IReadOnlyList<ServiceDescriptor>? _stackDescriptors;
    private IReadOnlyList<ServiceDescriptor>? _initializerDescriptors;
    private IServiceCollection? _stackServices;
    private bool _started;
    private IReadOnlyDictionary<string, CapturedTemplatePackages>? _templatePackages;

    internal async Task CaptureTemplatePackagesAsync(IReadOnlyList<WorkloadEntry> entries, IWorkloadPaths paths,
        IFileSystem files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(files);
        if (!_started || _completed is not null || _templatePackages is not null)
        {
            throw new InvalidOperationException("Native package inventory must be captured once during workload boot.");
        }

        Dictionary<string, CapturedTemplatePackages> captured = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkloadEntry entry in entries.Where(entry => entry.Kind == WorkloadKind.Workload && entry.BundledTemplatesDeclared))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion), BundledTemplateInstaller.ManifestName);
            CapturedTemplatePackages packages;
            try
            {
                string json = await files.ReadAllTextIfExistsAsync(path, cancellationToken)
                    ?? throw new FileNotFoundException("The installed native template declaration is missing.", path);
                BundledTemplatesManifest manifest = BundledTemplateInstaller.ReadManifest(json);
                packages = new(Array.AsReadOnly<NativeTemplatePackagePin>([.. manifest.Assets.Select(asset =>
                    new NativeTemplatePackagePin(asset.PackageId, NuGetVersion.Parse(asset.Version).ToNormalizedString(), asset.Sha256))]), null);
                string originPath = Path.Combine(paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion), BundledTemplateInstaller.OriginName);
                string originJson = await files.ReadAllTextIfExistsAsync(originPath, cancellationToken)
                    ?? throw new FileNotFoundException("The installed bundled-template origin is missing.", originPath);
                BundledTemplateOrigin origin = JsonSerializer.Deserialize(originJson, BundledTemplateJsonContext.Default.BundledTemplateOrigin)
                    ?? throw new JsonException("The installed bundled-template origin is empty.");
                if (!string.Equals(origin.PackageId, entry.PackageId, StringComparison.OrdinalIgnoreCase)
                    || origin.Version != entry.PackageVersion || origin.Source != entry.Source
                    || (entry.LogicalPackage is { } logical && (origin.LogicalPackageId != logical.PackageId
                        || origin.LogicalVersion != logical.PackageVersion || origin.LogicalSource != logical.Source)))
                {
                    throw new JsonException("The installed bundled-template origin does not match the captured workload ownership.");
                }
                packages = packages with
                {
                    Packages = Array.AsReadOnly<NativeTemplatePackagePin>([.. packages.Packages.Select(pin => pin with { Origin = origin })]),
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidWorkloadException)
            {
                packages = new([], $"Cannot read native template capability for '{entry.PackageId}@{entry.PackageVersion}': {exception.Message}");
            }
            captured.Add(PackageKey(entry.PackageId, entry.PackageVersion), packages);
        }
        _templatePackages = new ReadOnlyDictionary<string, CapturedTemplatePackages>(captured);
    }

    internal void Begin()
    {
        if (_started)
        {
            throw new InvalidOperationException("Native template workload capture cannot be reused for another boot.");
        }

        _started = true;
    }

    internal void Record(WorkloadEntry entry, RuntimeWorkloadInfo? workload, string? failure = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != WorkloadKind.Workload)
        {
            throw new ArgumentException("Native boot receipts describe selected runtime workloads only.", nameof(entry));
        }

        if (!_started || _completed is not null)
        {
            throw new InvalidOperationException("Native template workload registration is outside its active boot capture.");
        }

        if (workload is null && failure is null)
        {
            throw new ArgumentException("A successful configuration receipt requires a loaded runtime workload.", nameof(workload));
        }

        if (workload is not null && (!string.Equals(entry.PackageId, workload.PackageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(entry.PackageVersion, workload.PackageVersion, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The loaded workload does not match the selected registry entry.", nameof(workload));
        }

        if (entry.Aliases is null || (entry.LogicalPackage is { } logical
            && (string.IsNullOrWhiteSpace(logical.PackageId) || string.IsNullOrWhiteSpace(logical.PackageVersion) || logical.Aliases is null)))
        {
            throw new InvalidDataException("Selected runtime workload alias or logical package metadata is invalid.");
        }

        _registrations.Add(new(workload, entry.PackageId, entry.PackageVersion, Array.AsReadOnly<string>([.. entry.Aliases]),
            entry.LogicalPackage?.PackageId, entry.LogicalPackage?.PackageVersion,
            Array.AsReadOnly<string>([.. entry.LogicalPackage?.Aliases ?? []]), failure)
        {
            BundledTemplatesDeclared = entry.BundledTemplatesDeclared,
            TemplatePackages = _templatePackages?.GetValueOrDefault(PackageKey(entry.PackageId, entry.PackageVersion))?.Packages ?? [],
            TemplateDiagnostic = _templatePackages?.GetValueOrDefault(PackageKey(entry.PackageId, entry.PackageVersion))?.Diagnostic,
        });
    }

    internal void Complete()
    {
        if (!_started || _completed is not null)
        {
            throw new InvalidOperationException("Native template workload capture is not awaiting boot completion.");
        }

        _completed = Array.AsReadOnly<NativeTemplateWorkloadRegistration>([.. _registrations]);
    }

    internal void ObserveStackDescriptors(IServiceCollection services, bool configured, RuntimeWorkloadInfo? owner = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!_started || _completed is not null)
        {
            throw new InvalidOperationException("Native stack provenance is outside its active boot capture.");
        }

        if (_stackServices is not null && !ReferenceEquals(_stackServices, services))
        {
            throw new InvalidOperationException("Native stack provenance cannot span different service collections.");
        }

        _stackServices = services;
        foreach (ServiceDescriptor descriptor in StackDescriptors(services).Concat(InitializerDescriptors(services)))
        {
            _configuredStacks.TryAdd(descriptor, configured);
            _descriptorOwners.TryAdd(descriptor, owner);
        }
    }

    internal void Complete(IServiceCollection services)
    {
        ObserveStackDescriptors(services, configured: false);
        _stackDescriptors = Array.AsReadOnly<ServiceDescriptor>([.. StackDescriptors(services)]);
        _initializerDescriptors = Array.AsReadOnly<ServiceDescriptor>([.. InitializerDescriptors(services)]);
        Complete();
    }

    /// <summary>
    /// Selects actual stack instances only from successfully configured descriptor origins in the captured service collection.
    /// </summary>
    internal IReadOnlyList<IProjectStack> SelectConfiguredStacks(IReadOnlyList<IProjectStack> stacks, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        ArgumentNullException.ThrowIfNull(services);
        GetCompletedRegistrations();
        IReadOnlyList<ServiceDescriptor> captured = _stackDescriptors
            ?? throw new InvalidOperationException("Native stack selection requires descriptor provenance from the explicitly captured boot.");
        if (!ReferenceEquals(_stackServices, services))
        {
            throw new InvalidOperationException("Native stack selection requires the service collection used by the captured boot.");
        }

        ServiceDescriptor[] current = [.. StackDescriptors(services)];
        if (current.Length != captured.Count || current.Where((descriptor, index) => !ReferenceEquals(descriptor, captured[index])).Any())
        {
            throw new InvalidOperationException("Native stack registrations changed after workload boot capture. Rebuild the explicitly composed host.");
        }

        if (stacks.Count != captured.Count)
        {
            throw new InvalidOperationException("Resolved native stack enumeration does not match the captured service descriptor inventory.");
        }

        List<IProjectStack> configured = [];
        for (int index = 0; index < captured.Count; index++)
        {
            IProjectStack stack = stacks[index]
                ?? throw new InvalidOperationException("A captured stack descriptor resolved to null.");
            ServiceDescriptor descriptor = captured[index];
            if ((descriptor.ImplementationInstance is { } instance && !ReferenceEquals(instance, stack))
                || (descriptor.ImplementationType is { } type && !type.IsInstanceOfType(stack)))
            {
                throw new InvalidOperationException("Resolved native stack instances do not match their captured service descriptors.");
            }

            if (_configuredStacks[descriptor])
            {
                _stackOwners.TryAdd(stack, _descriptorOwners[descriptor]);
                configured.Add(stack);
            }
        }

        return configured.AsReadOnly();
    }

    internal IReadOnlyList<NativeTemplateWorkloadRegistration> GetCompletedRegistrations()
        => _completed ?? throw new InvalidOperationException(
            "Native template availability requires an explicitly completed workload boot capture. It cannot be inferred from an already booted host.");

    internal IReadOnlyList<IProjectStack> SelectConfiguredInitializerMetadata(IReadOnlyList<IProjectInitializer> initializers,
        IServiceCollection services, IReadOnlyList<IProjectStack> stacks)
    {
        ArgumentNullException.ThrowIfNull(initializers);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(stacks);
        GetCompletedRegistrations();
        IReadOnlyList<ServiceDescriptor> captured = _initializerDescriptors
            ?? throw new InvalidOperationException("Initializer metadata requires descriptor provenance from the captured boot.");
        ServiceDescriptor[] current = [.. InitializerDescriptors(services)];
        if (!ReferenceEquals(_stackServices, services) || current.Length != captured.Count
            || current.Where((descriptor, index) => !ReferenceEquals(descriptor, captured[index])).Any()
            || initializers.Count != captured.Count)
        {
            throw new InvalidOperationException("Initializer metadata changed after workload boot capture. Rebuild the host.");
        }

        List<IProjectStack> metadata = [];
        for (int index = 0; index < captured.Count; index++)
        {
            ServiceDescriptor descriptor = captured[index];
            IProjectInitializer initializer = initializers[index]
                ?? throw new InvalidOperationException("A captured initializer descriptor resolved to null.");
            if ((descriptor.ImplementationInstance is { } instance && !ReferenceEquals(instance, initializer))
                || (descriptor.ImplementationType is { } type && !type.IsInstanceOfType(initializer)))
            {
                throw new InvalidOperationException("Resolved initializer metadata does not match its captured descriptor.");
            }
            if (_configuredStacks[descriptor] && !stacks.Any(stack => string.Equals(stack.Stack, initializer.Stack, StringComparison.OrdinalIgnoreCase)))
            {
                InitializerProjectStackMetadata stack = new(initializer);
                _stackOwners.Add(stack, _descriptorOwners[descriptor]);
                metadata.Add(stack);
            }
        }
        return metadata.AsReadOnly();
    }

    internal NativeTemplateWorkloadRegistration? GetStackOrigin(IProjectStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return _stackOwners.TryGetValue(stack, out RuntimeWorkloadInfo? owner) && owner is not null
            ? GetCompletedRegistrations().Single(registration => ReferenceEquals(registration.Workload, owner)) : null;
    }

    internal NativeTemplatePackageScope? GetPackageScope(NativeTemplateOwnershipReader? ownership = null)
    {
        IReadOnlyList<NativeTemplateWorkloadRegistration> registrations = GetCompletedRegistrations();
        return _templatePackages is null ? null : new(
            Array.AsReadOnly<NativeTemplatePackagePin>([.. _templatePackages.Values.SelectMany(value => value.Packages)]),
            Array.AsReadOnly<NativeTemplatePackagePin>([.. registrations.Where(value => value.Workload is not null
                && value.Failure is null && value.TemplateDiagnostic is null).SelectMany(value => value.TemplatePackages)]), ownership);
    }

    private static string PackageKey(string id, string version) => $"{id}@{version}";

    private sealed record CapturedTemplatePackages(IReadOnlyList<NativeTemplatePackagePin> Packages, string? Diagnostic);

    private static IEnumerable<ServiceDescriptor> StackDescriptors(IServiceCollection services)
        => services.Where(descriptor => descriptor.ServiceType == typeof(IProjectStack) && !descriptor.IsKeyedService);

    private static IEnumerable<ServiceDescriptor> InitializerDescriptors(IServiceCollection services)
        => services.Where(descriptor => descriptor.ServiceType == typeof(IProjectInitializer) && !descriptor.IsKeyedService);
}

internal sealed record NativeTemplateWorkloadRegistration(
    RuntimeWorkloadInfo? Workload, string PackageId, string PackageVersion, IReadOnlyList<string> Aliases,
    string? LogicalPackageId, string? LogicalPackageVersion, IReadOnlyList<string> LogicalAliases, string? Failure)
{
    public bool BundledTemplatesDeclared { get; init; }

    public IReadOnlyList<NativeTemplatePackagePin> TemplatePackages { get; init; } = [];

    public string? TemplateDiagnostic { get; init; }
}
