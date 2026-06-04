// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Preview-only nudge that reminds users with a side-by-side Core Tools v4
/// install that v5 is invocable as <c>func5</c>. Removed at GA.
/// </summary>
internal sealed class FuncAliasNudge(
    IInteractionService interaction,
    ICliVersionProvider versionProvider)
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly ICliVersionProvider _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));

    /// <summary>
    /// Prints the nudge if the gates are satisfied. Never throws.
    /// </summary>
    /// <param name="exitCode">Exit code of the just-completed command.</param>
    /// <param name="commandName">Resolved command name (see <see cref="Telemetry.CommandNameResolver"/>).</param>
    public void TryPrint(int exitCode, string? commandName)
    {
        try
        {
            if (!ShouldPrint(exitCode, commandName))
            {
                return;
            }

            _interaction.WriteBlankLine();
            _interaction.WriteLine(line => line
                .Warning("Preview: ")
                .Muted("if you also have Core Tools (v4) installed, you may need to invoke the new CLI via '")
                .Command("func5")
                .Muted("' instead."));
        }
        catch
        {
            // Advisory output: never let a render failure surface as a
            // command failure.
        }
    }

    private bool ShouldPrint(int exitCode, string? commandName)
    {
        if (!_versionProvider.IsPrerelease)
        {
            return false;
        }

        if (!_interaction.IsInteractive)
        {
            return false;
        }

        // Failed commands: v4 running instead of v5 is a plausible cause.
        // "help" (bare `func` or explicit `func help`): the help text the
        // user is reading references `func ...` examples.
        return exitCode != 0
            || string.Equals(commandName, "help", StringComparison.OrdinalIgnoreCase);
    }
}
