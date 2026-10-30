// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Retains the function scaffolding command surface during the template engine migration.
/// </summary>
internal sealed class NewCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "Function name.",
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Template ID.",
    };

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Overwrite existing files.",
    };

    public Option<bool> NonInteractiveOption { get; } = new("--non-interactive")
    {
        Description = "Refuse to prompt.",
    };

    public Option<bool> ListOption { get; } = new("--list", "-l")
    {
        Description = "List available templates (temporarily unavailable).",
    };

    public Option<string?> FormatOption { get; } = new("--format")
    {
        Description = "Output mode (plain | json). Defaults to plain.",
    };

    private readonly IInteractionService _interaction;

    public NewCommand(IInteractionService interaction)
        : base("new", "Create a new function from a template (temporarily unavailable).")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

        AddPathArgument();
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(ForceOption);
        Options.Add(NonInteractiveOption);
        Options.Add(ListOption);
        Options.Add(FormatOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        _interaction.WriteError("Scaffolding and template listing are temporarily unavailable while the Azure Functions CLI migrates to the new templating engine.");
        return Task.FromResult(1);
    }
}
