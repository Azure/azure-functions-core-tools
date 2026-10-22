// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Retains exact selected runtime metadata and configuration outcomes during an explicitly captured boot.
/// </summary>
internal sealed class NativeTemplateWorkloadCapture
{
    private readonly List<NativeTemplateWorkloadRegistration> _registrations = [];
    private readonly Dictionary<ServiceDescriptor, bool> _configuredStacks = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<NativeTemplateWorkloadRegistration>? _completed;
    private IReadOnlyList<ServiceDescriptor>? _stackDescriptors;
    private IServiceCollection? _stackServices;
    private bool _started;

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
            Array.AsReadOnly<string>([.. entry.LogicalPackage?.Aliases ?? []]), failure));
    }

    internal void Complete()
    {
        if (!_started || _completed is not null)
        {
            throw new InvalidOperationException("Native template workload capture is not awaiting boot completion.");
        }

        _completed = Array.AsReadOnly<NativeTemplateWorkloadRegistration>([.. _registrations]);
    }

    internal void ObserveStackDescriptors(IServiceCollection services, bool configured)
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
        foreach (ServiceDescriptor descriptor in StackDescriptors(services))
        {
            _configuredStacks.TryAdd(descriptor, configured);
        }
    }

    internal void Complete(IServiceCollection services)
    {
        ObserveStackDescriptors(services, configured: false);
        _stackDescriptors = Array.AsReadOnly<ServiceDescriptor>([.. StackDescriptors(services)]);
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
                configured.Add(stack);
            }
        }

        return configured.AsReadOnly();
    }

    internal IReadOnlyList<NativeTemplateWorkloadRegistration> GetCompletedRegistrations()
        => _completed ?? throw new InvalidOperationException(
            "Native template availability requires an explicitly completed workload boot capture. It cannot be inferred from an already booted host.");

    private static IEnumerable<ServiceDescriptor> StackDescriptors(IServiceCollection services)
        => services.Where(descriptor => descriptor.ServiceType == typeof(IProjectStack) && !descriptor.IsKeyedService);
}

internal sealed record NativeTemplateWorkloadRegistration(
    RuntimeWorkloadInfo? Workload, string PackageId, string PackageVersion, IReadOnlyList<string> Aliases,
    string? LogicalPackageId, string? LogicalPackageVersion, IReadOnlyList<string> LogicalAliases, string? Failure);
