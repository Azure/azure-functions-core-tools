// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Creates a new function from a template. The full implementation requires
/// a language workload to be installed — this defines the command skeleton and options.
/// </summary>
public class NewCommand : BaseCommand
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
        _interaction.WriteMarkupLine(
            "[grey]Install a workload to create functions from templates:[/]");
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("  [white]func workload install dotnet[/]       [grey]C#, F#[/]");
        _interaction.WriteMarkupLine("  [white]func workload install node[/]         [grey]JavaScript, TypeScript[/]");
        _interaction.WriteMarkupLine("  [white]func workload install python[/]       [grey]Python[/]");
        _interaction.WriteMarkupLine("  [white]func workload install java[/]         [grey]Java[/]");
        _interaction.WriteMarkupLine("  [white]func workload install powershell[/]   [grey]PowerShell[/]");
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("[grey]Run[/] [white]func workload search[/] [grey]to discover available workloads.[/]");

        return Task.FromResult(1);
    }
}
