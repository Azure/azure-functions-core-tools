// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Reports a retained stack whose required local templates could not be registered.
/// </summary>
internal sealed class BundledTemplateRegistrationException(WorkloadEntry entry, Exception innerException)
    : Exception($"Stack '{(entry ?? throw new ArgumentNullException(nameof(entry))).PackageId}' {entry.PackageVersion} remains installed, " +
        $"but bundled-template registration failed: {(innerException ?? throw new ArgumentNullException(nameof(innerException))).Message} Retry the same stack install.", innerException)
{
    public WorkloadEntry Entry { get; } = entry;
}
