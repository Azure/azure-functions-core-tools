// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Surfaces a template's manual-instructions post action: it performs no work,
/// it just relays the declared instruction text to the user.
/// </summary>
internal sealed class ManualInstructionsPostActionHandler : IFuncPostActionHandler
{
    /// <inheritdoc />
    public Guid ActionId => FuncPostActionIds.ManualInstructions;

    /// <inheritdoc />
    public Task<FuncPostActionResult> ExecuteAsync(FuncPostActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string? instructions = context.PostAction.ManualInstructions;
        FuncPostActionResult result = string.IsNullOrWhiteSpace(instructions)
            ? new FuncPostActionResult.Succeeded()
            : new FuncPostActionResult.ManualInstructionsRequired(instructions);

        return Task.FromResult(result);
    }
}
