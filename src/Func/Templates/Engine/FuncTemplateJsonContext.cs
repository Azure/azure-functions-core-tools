// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Source-generated JSON context for func template host configuration.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    RespectNullableAnnotations = true,
    Converters = [typeof(FuncTemplateBooleanConverter)])]
[JsonSerializable(typeof(FuncTemplateHostData))]
[JsonSerializable(typeof(FuncTemplateSymbol))]
internal sealed partial class FuncTemplateJsonContext : JsonSerializerContext;
