// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Preview-only nudge that reminds users with a side-by-side Core Tools v4
/// install that v5 is invocable as <c>func5</c>. Removed at GA.
/// </summary>
/// <remarks>
/// <para>
/// The CLI ships hint text like "Run 'func init' to ..." which assumes the
/// running binary is reachable as <c>func</c>. In preview the installer
/// drops a <c>func5</c> shim alongside <c>func</c>; if v4 is also on PATH
/// it wins the bare name and those hints silently invoke v4. We don't
/// detect that situation; the message is phrased conditionally so it's
/// true for everyone in the preview audience.
/// </para>
/// <para>
/// Printed only when the user is likely to benefit: failed invocations
/// (where v4 running instead of v5 is a plausible cause) and the help
/// surface (bare <c>func</c> or explicit <c>func help</c>, where the
/// rendered help text references <c>func ...</c>). Successful commands
/// stay quiet so the banner doesn't become noise.
/// </para>
/// </remarks>
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
                .Muted("if you also have Core Tools v4 installed, invoke v5 as '")
                .Command("func5")
                .Muted("' instead of '")
                .Command("func")
                .Muted("'."));
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
