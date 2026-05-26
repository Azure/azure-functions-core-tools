// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands.Profile;

/// <summary>
/// Groups Azure Functions profile management commands.
/// </summary>
internal sealed class ProfileCommand : FuncCliCommand, IBuiltInCommand
{
    public ProfileCommand(ProfileListCommand listCommand, ProfileShowCommand showCommand)
        : base("profile", "Inspect and manage Azure Functions CLI profiles.")
    {
        ArgumentNullException.ThrowIfNull(listCommand);
        ArgumentNullException.ThrowIfNull(showCommand);

        Subcommands.Add(listCommand);
        Subcommands.Add(showCommand);
    }
}
