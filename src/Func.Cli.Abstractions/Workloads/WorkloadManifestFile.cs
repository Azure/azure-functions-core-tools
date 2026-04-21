// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Schema of the <c>workload.json</c> file that ships alongside a workload's
/// executable. The host reads this file during discovery to learn which
/// workload binary to launch and what runtimes/capabilities it supports —
/// without having to spawn the process.
/// </summary>
public sealed class WorkloadManifestFile
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Worker runtimes this workload supports (e.g., ["dotnet"], ["node"]).
    /// Used by the host to route requests.
    /// </summary>
    [JsonPropertyName("workerRuntimes")] public List<string> WorkerRuntimes { get; set; } = [];

    /// <summary>
    /// Languages this workload supports for display (e.g., ["C#", "F#"]).
    /// </summary>
    [JsonPropertyName("languages")] public List<string> Languages { get; set; } = [];

    /// <summary>
    /// Protocol version this workload speaks. Compared against
    /// <see cref="WorkloadProtocol.Version"/> by the host.
    /// </summary>
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = WorkloadProtocol.Version;

    /// <summary>
    /// Path to the workload executable, relative to the directory containing
    /// <c>workload.json</c>. May be a platform-specific binary
    /// (e.g., "func-workload-dotnet" or "func-workload-dotnet.exe").
    /// </summary>
    [JsonPropertyName("executable")] public string Executable { get; set; } = string.Empty;

    /// <summary>
    /// Optional extra arguments to prepend before any host-supplied args.
    /// Mostly useful for development (e.g., ["run","--project","Foo.csproj"]).
    /// </summary>
    [JsonPropertyName("executableArgs")] public List<string> ExecutableArgs { get; set; } = [];

    /// <summary>
    /// Capabilities this workload claims to implement. Must be a subset of
    /// what it actually returns from <c>initialize</c>; the host trusts the
    /// initialize response at runtime, but may use this for cheap pre-spawn
    /// routing.
    /// </summary>
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = [];

    /// <summary>
    /// File-name patterns that, if found in a directory, indicate this workload
    /// can handle the project (e.g., ["*.csproj", "*.fsproj"]).
    /// </summary>
    [JsonPropertyName("projectMarkers")] public List<string> ProjectMarkers { get; set; } = [];
}
