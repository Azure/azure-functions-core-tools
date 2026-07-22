// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for workload storage
/// shapes. Keeps the JSON path reflection-free (AOT/trim-friendly).
/// </summary>
/// <remarks>
/// We use a custom <see cref="JsonStringEnumConverter{T}"/> instead of
/// <c>UseStringEnumConverter = true</c> because the source generator's
/// generated converter does not apply <see cref="JsonNamingPolicy.CamelCase"/>
/// to enum value names; it would emit <c>"Workload"</c> rather than the
/// schema-required <c>"workload"</c>.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(WorkloadKindJsonConverter)])]
[JsonSerializable(typeof(WorkloadRegistry))]
[JsonSerializable(typeof(WorkloadMetadata))]
internal sealed partial class WorkloadJsonContext : JsonSerializerContext;

/// <summary>
/// Serializes <see cref="WorkloadKind"/> as a lowercase string
/// (<c>"workload"</c> / <c>"content"</c> / <c>"meta"</c>). The default
/// <see cref="JsonStringEnumConverter{TEnum}"/> would emit PascalCase.
/// </summary>
internal sealed class WorkloadKindJsonConverter() : JsonStringEnumConverter<WorkloadKind>(JsonNamingPolicy.CamelCase);

