// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console;

/// <summary>
/// One option for <see cref="IInteractionService.PromptForMultiSelectionAsync(string, IEnumerable{MultiSelectionChoice}, CancellationToken)"/>.
/// <see cref="Value"/> is what callers receive back; <see cref="Label"/> is what the
/// user sees (Spectre markup allowed).
/// </summary>
internal sealed record MultiSelectionChoice(string Value, string Label)
{
    public MultiSelectionChoice(string value)
        : this(value, value)
    {
    }
}
