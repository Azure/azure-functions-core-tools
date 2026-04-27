// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Initializes a new Azure Functions project. The full implementation requires
/// a language workload to be installed — this defines the command skeleton and options.
/// </summary>
public class InitCommand : BaseCommand
{
    public static readonly Option<string?> StackOption = new("--stack", "-s")
    {
        Description = "The stack (language / runtime) for the project"
    };

    public static readonly Option<string?> NameOption = new("--name", "-n")
    {
        Description = "The name of the function app project"
    };

    public static readonly Option<string?> LanguageOption = new("--language", "-l")
    {
        Description = "The programming language (e.g., C#, F#, JavaScript, TypeScript, Python)"
    };

    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Force initialization even if the folder is not empty"
    };

    private readonly IInteractionService _interaction;

    public InitCommand(IInteractionService interaction)
        : base("init", "Initialize a new Azure Functions project.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        _interaction = interaction;

        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        _interaction.WriteError("No language workloads installed.");
        _interaction.WriteBlankLine();
        _interaction.WriteHint("Install a workload to initialize a project:");
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
