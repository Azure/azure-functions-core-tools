// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Selection;

/// <summary>
/// Supplies neutral filters and template-only tokens, with precedence narrowing explicitly requested by the caller.
/// </summary>
internal sealed record TemplateSelectionRequest(
    string Reference,
    TemplateType Type,
    ImmutableArray<string> Tokens,
    ImmutableArray<string> ReservedAliases,
    string? Language = null,
    bool ApplyPrecedence = false);
