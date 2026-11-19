// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Represents a symbol in a function template from func.host.json.
/// </summary>
/// <param name="Name">The name of the symbol.</param>
internal sealed record FuncTemplateSymbol(string Name)
{
    /// <summary>
    /// Gets a value indicating whether the symbol is hidden.
    /// </summary>
    public bool IsHidden { get; init; }

    /// <summary>
    /// Gets a value indicating whether the symbol should always be shown.
    /// </summary>
    public bool AlwaysShow { get; init; }

    /// <summary>
    /// Gets the long name of the symbol.
    /// </summary>
    public string? LongName { get; init; }

    /// <summary>
    /// Gets the short name of the symbol.
    /// </summary>
    public string? ShortName { get; init; }
}
