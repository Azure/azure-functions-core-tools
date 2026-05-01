// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// System.Text.Json source-generated serialization context for the workload
/// storage shapes. Build-time generation keeps the JSON path reflection-free
/// (AOT/trim-friendly) and centralizes naming policy so individual properties
/// don't carry <c>[JsonPropertyName]</c> attributes.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GlobalManifest))]
internal sealed partial class WorkloadJsonContext : JsonSerializerContext;
