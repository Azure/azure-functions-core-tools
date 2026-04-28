// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;

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

    private readonly IWorkloadHintRenderer _hintRenderer;

    public NewCommand(IWorkloadHintRenderer hintRenderer)
        : base("new", "Create a new function from a template.")
    {
        ArgumentNullException.ThrowIfNull(hintRenderer);
        _hintRenderer = hintRenderer;

        AddPathArgument();
        Options.Add(NameOption);
        Options.Add(TemplateOption);
        Options.Add(ForceOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        // Until a workload contributes templates, the only useful thing this
        // command can do is point the user at `func workload install`.
        _hintRenderer.Render(new WorkloadHint(
            WorkloadHintKind.NoWorkloadsInstalled,
            ActionDescription: "create functions from templates",
            RequestedStack: null,
            InstalledStacks: Array.Empty<string>()));

        return Task.FromResult(1);
    }
}
