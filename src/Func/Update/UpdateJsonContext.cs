// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for GitHub releases
/// deserialization. Keeps JSON reflection-free (AOT/trim-friendly), matching
/// the pattern used by other JSON-consuming subsystems in this CLI.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GitHubRelease[]))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
