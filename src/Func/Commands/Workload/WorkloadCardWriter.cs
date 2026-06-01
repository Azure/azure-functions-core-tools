// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Renders a workload as a definition-list "card": a heading line followed
/// by aligned label/value rows and a full-width description block. Shared
/// between <c>func workload search</c> and <c>func workload list --verbose</c>
/// so both commands present the same card shape and theme palette (labels
/// in the command accent colour, values muted).
/// </summary>
internal sealed class WorkloadCardWriter(IInteractionService interaction)
{
    // Widest label in the card layout. Used to align values across rows.
    private const int LabelColumnWidth = 13;

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public void WriteHeading(string heading)
        => _interaction.WriteLine(line => line.Heading(heading));

    public void WriteField(string label, string value)
    {
        string padded = (label + ":").PadRight(LabelColumnWidth);
        _interaction.WriteLine(line => line.Command(padded).Muted(value));
    }

    public void WriteDescription(string? description)
    {
        string text = string.IsNullOrWhiteSpace(description)
            ? "(no description)"
            : description!.Trim();
        WriteField("Description", text);
    }

    public void WriteAliases(IReadOnlyList<string> aliases)
    {
        string label = aliases.Count > 1 ? "Aliases" : "Alias";
        string value = aliases.Count == 0 ? string.Empty : string.Join(", ", aliases);
        WriteField(label, value);
    }

    public void WriteSeparator() => _interaction.WriteBlankLine();
}
