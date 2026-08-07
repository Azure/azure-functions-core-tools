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
/// The manifest's <c>kind</c> wire values are not derivable from a naming policy
/// (<c>rid-pointer</c> is hyphenated), so <see cref="WorkloadKindJsonConverter"/>
/// maps them explicitly instead of using <c>UseStringEnumConverter</c>.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(WorkloadKindJsonConverter)])]
[JsonSerializable(typeof(WorkloadRegistry))]
[JsonSerializable(typeof(WorkloadMetadata))]
internal sealed partial class WorkloadJsonContext : JsonSerializerContext;

/// <summary>
/// Serializes <see cref="WorkloadKind"/> using the workload manifest's wire values.
/// </summary>
internal sealed class WorkloadKindJsonConverter : JsonConverter<WorkloadKind>
{
    public override WorkloadKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        if (!WorkloadKind.TryParseWireValue(value, out WorkloadKind kind))
        {
            throw new JsonException($"Unknown workload kind '{value}'.");
        }

        return kind;
    }

    public override void Write(Utf8JsonWriter writer, WorkloadKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToWireValue());
    }
}
