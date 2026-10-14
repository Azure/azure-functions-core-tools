// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Reports settled cancellation after stack admission so command boundaries can expose the retained partial state.
/// </summary>
internal sealed class BundledTemplateRegistrationCanceledException(WorkloadEntry entry, OperationCanceledException innerException,
    CancellationToken cancellationToken)
    : OperationCanceledException($"Stack '{(entry ?? throw new ArgumentNullException(nameof(entry))).PackageId}' {entry.PackageVersion} remains installed, " +
        "but bundled-template registration was canceled. Retry the same stack install.", innerException ?? throw new ArgumentNullException(nameof(innerException)), cancellationToken)
{
    public WorkloadEntry Entry { get; } = entry;
}
