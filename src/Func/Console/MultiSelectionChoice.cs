// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console;

/// <summary>
/// One option for <see cref="IInteractionService.PromptForMultiSelectionAsync(string, IEnumerable{MultiSelectionChoice}, CancellationToken)"/>.
/// <see cref="Value"/> is what callers receive back; <see cref="Label"/> is what the user sees.
/// </summary>
/// <remarks>
/// Splitting display text from the underlying value lets callers decorate
/// choices (for example, with an "(installed)" suffix) without leaking
/// presentation concerns into the value the caller acts on.
/// </remarks>
internal sealed record MultiSelectionChoice(string Value, string Label)
{
    public MultiSelectionChoice(string value)
        : this(value, value)
    {
    }
}
