// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for quickstart manifest
/// deserialization. Keeps JSON reflection-free (AOT/trim-friendly).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(QuickstartManifestEnvelope))]
[JsonSerializable(typeof(ManifestCacheMeta))]
internal sealed partial class QuickstartJsonContext : JsonSerializerContext;
