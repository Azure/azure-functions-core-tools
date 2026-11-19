// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Describes func-specific template presentation and parameter aliases.
/// </summary>
internal sealed class FuncTemplateHostData
{
    /// <summary>
    /// Gets an empty instance of <see cref="FuncTemplateHostData"/>.
    /// </summary>
    public static FuncTemplateHostData Empty { get; } = new();

    /// <summary>
    /// Gets the symbol information for the template.
    /// </summary>
    [JsonConverter(typeof(FuncTemplateSymbolsConverter))]
    public IReadOnlyDictionary<string, FuncTemplateSymbol> SymbolInfo { get; init; } = FrozenDictionary<string, FuncTemplateSymbol>.Empty;

    /// <summary>
    /// Gets the usage examples for the template.
    /// </summary>
    public IReadOnlyList<string> UsageExamples { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the template is hidden.
    /// </summary>
    public bool IsHidden { get; init; }

    /// <summary>
    /// Parses dotnet-shaped host metadata, rejecting invalid supported fields.
    /// </summary>
    /// <exception cref="JsonException">The JSON or a supported field has an invalid shape.</exception>
    public static FuncTemplateHostData Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return JsonSerializer.Deserialize(json, FuncTemplateJsonContext.Default.FuncTemplateHostData)
            ?? throw new JsonException("Host configuration must be a JSON object.");
    }
}
