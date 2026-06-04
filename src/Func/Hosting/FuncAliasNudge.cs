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
/// detect that situation (filesystem probe in the hot path isn't worth
/// it), instead the message is phrased conditionally so it's true for
/// everyone in the preview audience.
/// </para>
/// <para>
/// Printed only after commands that themselves emit <c>func ...</c> hints
/// (see <see cref="ShouldShowFor"/>), so it sits next to the text it
/// clarifies. Best-effort: render failures never affect the exit code.
/// </para>
/// </remarks>
internal sealed class FuncAliasNudge(
    IInteractionService interaction,
    ICliVersionProvider versionProvider)
{
    // Top-level commands whose output references `func ...` somewhere
    // (init/new/setup/start hints, workload subcommand suggestions, help
    // text, profile errors). `version` is excluded because it only prints
    // the version string and nothing else.
    private static readonly HashSet<string> _allowedRootCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "help",
            "init",
            "new",
            "profile",
            "setup",
            "start",
            "workload",
            "unknown", // CommandNameResolver fallback for parse errors
        };

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly ICliVersionProvider _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));

    /// <summary>
    /// Prints the nudge if the gates are satisfied. Never throws.
    /// </summary>
    public void TryPrint(string? commandName)
    {
        try
        {
            if (!ShouldPrint(commandName))
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

    private bool ShouldPrint(string? commandName)
    {
        if (!_versionProvider.IsPrerelease)
        {
            return false;
        }

        if (!_interaction.IsInteractive)
        {
            return false;
        }

        return ShouldShowFor(commandName);
    }

    private static bool ShouldShowFor(string? commandName)
    {
        if (string.IsNullOrEmpty(commandName))
        {
            return true;
        }

        int spaceIndex = commandName.IndexOf(' ');
        string root = spaceIndex < 0 ? commandName : commandName[..spaceIndex];
        return _allowedRootCommands.Contains(root);
    }
}
