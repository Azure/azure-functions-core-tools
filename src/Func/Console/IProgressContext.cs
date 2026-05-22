// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console;

/// <summary>
/// Mutable handle on an in-flight progress display. Passed by
/// <see cref="IInteractionService.RunWithProgressAsync{T}"/> to the action so
/// long-running work can update its description and (optionally) report
/// completion against a known total.
/// </summary>
/// <remarks>
/// Implementations must be safe to call from the same logical flow that owns
/// the running action; concurrent updates from background tasks are not
/// required to be thread-safe. In non-interactive mode the implementation
/// typically degrades to writing a single line per description change and
/// ignores numeric updates.
/// </remarks>
internal interface IProgressContext
{
    /// <summary>
    /// Updates the visible description for the in-flight task. Pass a short
    /// present-tense phrase (e.g. <c>"Downloading workload..."</c>).
    /// </summary>
    public void SetDescription(string description);

    /// <summary>
    /// Sets the total expected work units. Pass <c>null</c> for indeterminate
    /// progress (no percentage). Switching to a non-null value enables
    /// percentage / "X of Y" display.
    /// </summary>
    public void SetTotal(double? total);

    /// <summary>
    /// Reports the absolute current value (between 0 and the configured
    /// total). Ignored when no total has been set.
    /// </summary>
    public void Report(double value);

    /// <summary>
    /// Convenience: increments the current value by <paramref name="amount"/>.
    /// </summary>
    public void Increment(double amount);
}
