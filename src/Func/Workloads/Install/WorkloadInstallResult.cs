// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Outcome of <see cref="IWorkloadInstaller.InstallFromPackageAsync"/>.
/// </summary>
/// <param name="Entry">The registry entry for the installed workload.</param>
/// <param name="AlreadyInstalled">
/// <see langword="true"/> when the same (<c>packageId</c>, <c>version</c>)
/// was already installed and the call was a no-op.
/// </param>
internal sealed record WorkloadInstallResult(WorkloadEntry Entry, bool AlreadyInstalled);
