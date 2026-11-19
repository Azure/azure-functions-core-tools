// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console;

internal sealed class UnexpectedErrorReporter(IInteractionService interaction)
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public void Report(Exception exception, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        _interaction.WriteError($"An unexpected error occurred: {message}");
        if (verbose)
        {
            _interaction.WriteError(exception.ToString());
        }
    }
}
