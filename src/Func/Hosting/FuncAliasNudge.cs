// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Preview-only nudge that tells the user to invoke v5 as <c>func5</c>
/// when another <c>func</c> wins PATH (typically a side-by-side Core
/// Tools v4 install). Removed at GA along with <see cref="IFuncInvocation.ConflictDetected"/>.
/// </summary>
/// <remarks>
/// Printed once per invocation, immediately after the version-update
/// notice. Best-effort: any failure is swallowed so this never affects
/// the exit code of the user's command.
/// </remarks>
internal sealed class FuncAliasNudge(
    IInteractionService interaction,
    IFuncInvocation invocation,
    ICliVersionProvider versionProvider)
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IFuncInvocation _invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
    private readonly ICliVersionProvider _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));

    /// <summary>
    /// Prints the nudge if all gates are satisfied. Never throws.
    /// </summary>
    public void TryPrint()
    {
        try
        {
            if (!ShouldPrint())
            {
                return;
            }

            _interaction.WriteBlankLine();
            _interaction.WriteLine(line => line
                .Warning("Preview: ")
                .Muted("another '")
                .Command("func")
                .Muted($"' is earlier on your PATH ({_invocation.ConflictingPath}). Use '")
                .Command("func5")
                .Muted("' to run v5 commands shown above."));
        }
        catch
        {
            // Advisory output: never let a render failure surface as a
            // command failure.
        }
    }

    private bool ShouldPrint()
    {
        if (!_versionProvider.IsPrerelease)
        {
            return false;
        }

        if (!_interaction.IsInteractive)
        {
            return false;
        }

        return _invocation.ConflictDetected;
    }
}
