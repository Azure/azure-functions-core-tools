// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Tests.Commands;

internal sealed class HelpMetadataValidator
{
    public IReadOnlyList<string> FindViolations(Command root)
    {
        ArgumentNullException.ThrowIfNull(root);

        List<string> violations = [];
        InspectCommand(root, "func", violations);
        return violations;
    }

    private static void InspectCommand(Command command, string path, List<string> violations)
    {
        AddViolationIfInvalid(command.Description, $"{path}: command", violations);

        foreach (Option option in command.Options)
        {
            AddViolationIfInvalid(option.Description, $"{path} {option.Name}: option", violations);
        }

        foreach (Argument argument in command.Arguments)
        {
            AddViolationIfInvalid(argument.Description, $"{path} <{argument.Name}>: argument", violations);
        }

        foreach (Command subcommand in command.Subcommands)
        {
            InspectCommand(subcommand, $"{path} {subcommand.Name}", violations);
        }
    }

    private static void AddViolationIfInvalid(string? description, string path, List<string> violations)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Contains('\r') || description.Contains('\n'))
        {
            violations.Add(path);
        }
    }
}
