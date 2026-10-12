// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Source-generated JSON metadata for the installer-authored installation contract.
/// </summary>
[JsonSourceGenerationOptions(
    AllowDuplicateProperties = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(InstallationMetadataDocument))]
internal sealed partial class InstallationMetadataJsonContext : JsonSerializerContext;
