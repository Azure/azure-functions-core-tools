// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads;

// JSON-RPC 2.0 envelope types and the request/result/notification payload
// records exchanged between the host and a workload.
//
// All payload records are immutable and AOT-friendly: only public properties,
// no reflection-required constructors. Use System.Text.Json source generation
// via WorkloadJsonContext when (de)serializing in AOT scenarios.

/// <summary>
/// JSON-RPC 2.0 request envelope sent host → workload.
/// </summary>
public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = WorkloadProtocol.JsonRpcVersion;
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; } = string.Empty;
    [JsonPropertyName("params")] public JsonElement? Params { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 response envelope sent workload → host.
/// Exactly one of <see cref="Result"/> or <see cref="Error"/> is populated.
/// </summary>
public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = WorkloadProtocol.JsonRpcVersion;
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("result")] public JsonElement? Result { get; set; }
    [JsonPropertyName("error")] public JsonRpcError? Error { get; set; }
}

public sealed class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }
}

// ---- initialize ----

public sealed record InitializeParams(
    [property: JsonPropertyName("hostVersion")] string HostVersion,
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("cwd")] string Cwd);

public sealed record InitializeResult(
    [property: JsonPropertyName("workloadId")] string WorkloadId,
    [property: JsonPropertyName("workloadVersion")] string WorkloadVersion,
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("supportedRuntimes")] IReadOnlyList<string> SupportedRuntimes);

// ---- project.detect ----

public sealed record ProjectDetectParams(
    [property: JsonPropertyName("directory")] string Directory);

public sealed record ProjectDetectResult(
    [property: JsonPropertyName("matched")] bool Matched,
    [property: JsonPropertyName("runtime")] string? Runtime,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("confidence")] double Confidence);

// ---- project.init ----

public sealed record ProjectInitParams(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("workerRuntime")] string WorkerRuntime,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("projectName")] string? ProjectName,
    [property: JsonPropertyName("force")] bool Force,
    [property: JsonPropertyName("extra")] IReadOnlyDictionary<string, string>? Extra);

public sealed record ProjectInitResult(
    [property: JsonPropertyName("filesCreated")] IReadOnlyList<string> FilesCreated);

// ---- templates.list ----

public sealed record TemplatesListParams(
    [property: JsonPropertyName("language")] string? Language);

public sealed record FunctionTemplateInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("workerRuntime")] string WorkerRuntime,
    [property: JsonPropertyName("language")] string? Language);

public sealed record TemplatesListResult(
    [property: JsonPropertyName("templates")] IReadOnlyList<FunctionTemplateInfo> Templates);

// ---- templates.create ----

public sealed record TemplatesCreateParams(
    [property: JsonPropertyName("templateName")] string TemplateName,
    [property: JsonPropertyName("functionName")] string FunctionName,
    [property: JsonPropertyName("outputPath")] string OutputPath,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("authLevel")] string? AuthLevel,
    [property: JsonPropertyName("force")] bool Force);

public sealed record TemplatesCreateResult(
    [property: JsonPropertyName("filesCreated")] IReadOnlyList<string> FilesCreated);

// ---- pack.run ----

public sealed record PackRunParams(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("outputPath")] string? OutputPath,
    [property: JsonPropertyName("noBuild")] bool NoBuild);

public sealed record PackRunResult(
    [property: JsonPropertyName("outputPath")] string OutputPath);
