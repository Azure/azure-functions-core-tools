// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// <c>func new</c> — scaffolds a new function from an installed templates
/// content workload, or lists available templates when <c>--list</c> is
/// supplied. Wires the command surface and delegates to
/// <see cref="NewCommandRunner"/>, which currently terminates at
/// the engine-dispatch step until <see cref="ITemplateEngineProvider"/>
/// implementations exist.
/// </summary>
internal sealed class NewCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "Function name. Defaults to the template's default function name.",
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Template ID. Omit in an interactive shell to pick from a list.",
    };

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Overwrite existing files.",
    };

    public Option<bool> NonInteractiveOption { get; } = new("--non-interactive")
    {
        Description = "Refuse to prompt; exit 1 if any required input is missing.",
    };

    public Option<bool> ListOption { get; } = new("--list", "-l")
    {
        Description = "List available templates for this project instead of scaffolding.",
    };

    private readonly NewCommandRunner _runner;

    public NewCommand(NewCommandRunner runner)
        : base("new", "Create a new function from a template.")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));

        AddPathArgument();
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(ForceOption);
        Options.Add(NonInteractiveOption);
        Options.Add(ListOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        var invocation = new NewInvocation(
            workingDirectory,
            RequestedTemplate: parseResult.GetValue(TemplateOption),
            RequestedFunctionName: parseResult.GetValue(NameOption),
            Force: parseResult.GetValue(ForceOption),
            NonInteractive: parseResult.GetValue(NonInteractiveOption));

        return parseResult.GetValue(ListOption)
            ? _runner.ListAsync(invocation, cancellationToken)
            : _runner.ExecuteAsync(invocation, cancellationToken);
    }
}
