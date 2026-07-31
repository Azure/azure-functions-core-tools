// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// The engine-inert <c>func.host.json</c> that ships beside a template's
/// <c>template.json</c>. Supplies CLI-only presentation hints (option
/// aliases, hidden flags, validator regexes) the engine schema can't express.
/// </summary>
/// <param name="Symbols">Per-symbol hints, keyed by template symbol id.</param>
/// <param name="FunctionNameValidator">Optional validator for the function name (<c>--name</c>).</param>
internal sealed record FuncHostFile(
    IReadOnlyList<FuncHostSymbolInfo> Symbols,
    FuncHostValidator? FunctionNameValidator)
{
    /// <summary>
    /// An empty host file (no symbols, no function-name validator).
    /// </summary>
    internal static FuncHostFile Empty { get; } = new([], null);

    /// <summary>
    /// Returns the hints for symbol <paramref name="id"/>, or <c>null</c> when
    /// the host file declares none.
    /// </summary>
    internal FuncHostSymbolInfo? FindSymbol(string id)
        => Symbols.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// CLI presentation hints for a single template symbol.
/// </summary>
/// <param name="Id">The template symbol id these hints apply to.</param>
/// <param name="LongName">Option long name without the <c>--</c> prefix, or <c>null</c> to derive from the id.</param>
/// <param name="IsHidden">When true, the symbol hydrates no CLI option.</param>
/// <param name="Validator">Optional value validator.</param>
internal sealed record FuncHostSymbolInfo(
    string Id,
    string? LongName,
    bool IsHidden,
    FuncHostValidator? Validator);

/// <summary>
/// A regex validator with the message shown when a value fails it.
/// </summary>
/// <param name="Expression">The regular expression the value must match.</param>
/// <param name="ErrorText">The message shown on failure, or <c>null</c>.</param>
internal sealed record FuncHostValidator(string Expression, string? ErrorText);
