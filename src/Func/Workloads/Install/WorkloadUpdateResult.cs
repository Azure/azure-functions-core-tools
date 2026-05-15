// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Outcome of <see cref="IWorkloadInstaller.UpdateAsync"/>.
/// </summary>
/// <param name="Entry">
/// Registry entry now in place for the workload. When
/// <see cref="NoUpdateAvailable"/> is <c>true</c>, this is the existing
/// entry (untouched); otherwise it is the freshly written entry for the
/// new version.
/// </param>
/// <param name="PreviousVersion">
/// The version that was installed before the update. Equal to
/// <see cref="WorkloadEntry.PackageVersion"/> on <see cref="Entry"/> when
/// <see cref="NoUpdateAvailable"/> is <c>true</c>.
/// </param>
/// <param name="NoUpdateAvailable">
/// <see langword="true"/> when the catalog had no candidate higher than
/// the currently installed version under the requested policy; the call
/// was a no-op.
/// </param>
/// <param name="NoCandidateOnSource">
/// <see langword="true"/> when the configured source returned no version
/// at all for the package (typically because the package isn't published
/// there). Implies <see cref="NoUpdateAvailable"/>; lets the command
/// distinguish "package missing on source" from "found but not newer".
/// </param>
internal sealed record WorkloadUpdateResult(
    WorkloadEntry Entry,
    string PreviousVersion,
    bool NoUpdateAvailable,
    bool NoCandidateOnSource = false);
