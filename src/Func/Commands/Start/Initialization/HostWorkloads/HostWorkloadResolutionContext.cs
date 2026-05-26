// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Inputs used to resolve the host workload for a start run.
/// </summary>
internal sealed record HostWorkloadResolutionContext(
    string? RequestedHostVersion,
    VersionRange? ProfileHostVersionRange,
    bool Offline);
