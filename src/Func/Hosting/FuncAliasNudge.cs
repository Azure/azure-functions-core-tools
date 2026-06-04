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
/// (where v4 running instead of v5 is a plausible cause) and bare
/// <c>func</c> (where help text references <c>func ...</c>). Successful
/// commands stay quiet so the banner doesn't become noise.
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
    /// <param name="isBareInvocation"><c>true</c> when the user typed <c>func</c> with no arguments.</param>
    public void TryPrint(int exitCode, bool isBareInvocation)
    {
        try
        {
            if (!ShouldPrint(exitCode, isBareInvocation))
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

    private bool ShouldPrint(int exitCode, bool isBareInvocation)
    {
        if (!_versionProvider.IsPrerelease)
        {
            return false;
        }

        if (!_interaction.IsInteractive)
        {
            return false;
        }

        return exitCode != 0 || isBareInvocation;
    }
}
