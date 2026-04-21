// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Source-generated context for host-only types (the installed-workloads
/// manifest). Wire-format types use <see cref="WorkloadJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InstalledWorkloadsManifest))]
[JsonSerializable(typeof(InstalledWorkloadInfo))]
internal partial class HostJsonContext : JsonSerializerContext
{
}
