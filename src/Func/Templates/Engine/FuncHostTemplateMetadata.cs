// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Aliases and visibility a template declares for the func host in <c>.template.config/func.host.json</c>.
/// </summary>
/// <param name="IsHidden">Whether the template is hidden from ordinary listings.</param>
/// <param name="Symbols">Symbol metadata keyed by canonical symbol name, which is case-sensitive.</param>
internal sealed record FuncHostTemplateMetadata(bool IsHidden, IReadOnlyDictionary<string, FuncHostSymbolMetadata> Symbols)
{
    public static FuncHostTemplateMetadata Empty { get; } = new(false, ReadOnlyDictionary<string, FuncHostSymbolMetadata>.Empty);
}

/// <summary>
/// Func host metadata for one template symbol.
/// </summary>
/// <param name="LongName">Long alias without the <c>--</c> prefix, or <c>null</c> to use the canonical name.</param>
/// <param name="ShortName">Short alias without the <c>-</c> prefix, or <c>null</c> when it's missing, empty or null.</param>
/// <param name="IsHidden">Whether the symbol is hidden from ordinary details while staying parseable.</param>
/// <param name="AlwaysShow">Whether the symbol always appears in combined help.</param>
internal sealed record FuncHostSymbolMetadata(string? LongName, string? ShortName, bool IsHidden, bool AlwaysShow);
