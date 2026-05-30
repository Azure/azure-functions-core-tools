// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console;

/// <summary>
/// One option for <see cref="IInteractionService.PromptForMultiSelectionAsync(string, IEnumerable{MultiSelectionChoice}, CancellationToken)"/>.
/// <see cref="Value"/> is what callers receive back; <see cref="Label"/> is what the
/// user sees (Spectre markup allowed). Set <see cref="IsPreselected"/> to render the
/// choice checked when the prompt opens. Set <see cref="IsDisabled"/> to surface a
/// read-only entry: it still renders in the list (and is dropped from the result),
/// so callers can show "already-done" items in context without offering them as
/// actionable choices.
/// </summary>
internal sealed record MultiSelectionChoice(string Value, string Label)
{
    public MultiSelectionChoice(string value)
        : this(value, value)
    {
    }

    public bool IsPreselected { get; init; }

    public bool IsDisabled { get; init; }
}
