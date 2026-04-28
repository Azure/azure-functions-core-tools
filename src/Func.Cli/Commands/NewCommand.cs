// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Creates a new function from a template. The full implementation requires
/// a language workload to be installed — this defines the command skeleton and options.
/// </summary>
internal class NewCommand : BaseCommand, IBuiltInCommand
{
    public static readonly Option<string?> NameOption = new("--name", "-n")
    {
        Description = "The name of the function"
    };

    public static readonly Option<string?> TemplateOption = new("--template", "-t")
    {
        Description = "The function template name"
    };

    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Overwrite existing files"
    };

    private readonly IInteractionService _interaction;

    public NewCommand(IInteractionService interaction)
        : base("new", "Create a new function from a template.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        _interaction = interaction;

        AddPathArgument();
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(ForceOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        _interaction.WriteError("No language workloads installed.");
        _interaction.WriteBlankLine();
        _interaction.WriteHint("Install a workload to create functions from templates:");
        _interaction.WriteBlankLine();
        Stacks.WriteWorkloadInstallHints(_interaction);
        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l
            .Muted("Run ")
            .Command("func workload search")
            .Muted(" to discover available workloads."));

        return Task.FromResult(1);
    }
}
