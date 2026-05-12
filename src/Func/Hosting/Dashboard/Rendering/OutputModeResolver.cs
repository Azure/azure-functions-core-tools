// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Picks the effective <see cref="OutputMode"/> for <c>func start</c> from
/// explicit flags + auto-detection. Mode selection rules (locked in the
/// design plan):
/// <list type="number">
///   <item>Explicit <c>--output</c> always wins.</item>
///   <item><c>--no-tui</c> is an alias for <c>--output=plain</c>.</item>
///   <item>Auto: non-TTY or <c>CI</c> env var → <c>plain</c>; otherwise <c>compact</c>.</item>
///   <item>JSON is never auto-selected — callers must opt in.</item>
/// </list>
/// </summary>
internal static class OutputModeResolver
{
    public static OutputMode Resolve(OutputMode? explicitMode, bool noTui, IInteractionService interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        if (explicitMode is { } mode)
        {
            return mode;
        }

        if (noTui)
        {
            return OutputMode.Plain;
        }

        return interaction.IsInteractive ? OutputMode.Compact : OutputMode.Plain;
    }

    /// <summary>
    /// Final guard: even when the user explicitly asked for compact, a
    /// non-interactive stdout cannot host Spectre's <c>LiveDisplay</c>.
    /// Returns the original mode if no downgrade is needed; otherwise
    /// returns the safe fallback and indicates whether a downgrade was
    /// applied so the caller can surface a one-line stderr notice.
    /// </summary>
    public static OutputMode ApplyTerminalSafetyFallback(OutputMode mode, IInteractionService interaction, out bool downgraded)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        downgraded = false;

        if (mode == OutputMode.Compact && !interaction.IsInteractive)
        {
            downgraded = true;
            return OutputMode.Plain;
        }

        return mode;
    }

    public static bool TryParse(string? value, out OutputMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "compact":
                mode = OutputMode.Compact;
                return true;
            case "plain":
                mode = OutputMode.Plain;
                return true;
            case "json":
                mode = OutputMode.Json;
                return true;
            default:
                mode = default;
                return false;
        }
    }
}
