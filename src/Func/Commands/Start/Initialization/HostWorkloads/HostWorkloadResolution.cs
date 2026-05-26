// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Result of resolving the host workload for a start run.
/// </summary>
internal abstract record HostWorkloadResolution(string HostVersion)
{
    public sealed record Installed(ContentWorkloadInfo Workload, NuGetVersion Version, bool ExplicitlyRequested)
        : HostWorkloadResolution(Version.ToNormalizedString());

    public sealed record InstallRequired(string Version, string Message, string? PackageId = null)
        : HostWorkloadResolution(Version);
}
