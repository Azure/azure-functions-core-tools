// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Routes initialization warnings to the CLI interaction service.
/// </summary>
internal sealed class ProjectInitializationReporter(IInteractionService interaction) : IProjectInitializationReporter
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public void WriteWarning(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _interaction.WriteWarning(message);
    }
}
