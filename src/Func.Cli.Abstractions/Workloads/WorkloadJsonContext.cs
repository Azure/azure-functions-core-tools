// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// for all wire-format types. Using source generation keeps the protocol
/// AOT-compatible end-to-end (host and workload SDK).
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ProjectDetectParams))]
[JsonSerializable(typeof(ProjectDetectResult))]
[JsonSerializable(typeof(ProjectInitParams))]
[JsonSerializable(typeof(ProjectInitResult))]
[JsonSerializable(typeof(TemplatesListParams))]
[JsonSerializable(typeof(TemplatesListResult))]
[JsonSerializable(typeof(FunctionTemplateInfo))]
[JsonSerializable(typeof(TemplatesCreateParams))]
[JsonSerializable(typeof(TemplatesCreateResult))]
[JsonSerializable(typeof(PackRunParams))]
[JsonSerializable(typeof(PackRunResult))]
[JsonSerializable(typeof(WorkloadManifestFile))]
public partial class WorkloadJsonContext : JsonSerializerContext
{
}
