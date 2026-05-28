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
internal sealed class NewCommand : FuncCliCommand, IBuiltInCommand, ITemplateAwareHelpProvider
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
    private readonly HashSet<string> _builtInOptionNames;

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

        // Snapshot the built-in option names so the help-time hydration
        // pass can tell which Options on the command came from the user's
        // chosen template (added on the fly) and which were here from the
        // start. Without this, repeated `func new -t X --help` invocations
        // would accumulate stale hydrated options on the singleton command.
        _builtInOptionNames = new HashSet<string>(
            Options.Select(o => o.Name),
            StringComparer.Ordinal);
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

    /// <summary>
    /// Stage-B help hook: when <c>func new --template &lt;id&gt; --help</c>
    /// fires, the renderer calls this so the command can dynamically attach
    /// the chosen template's hydrated options before help is rendered.
    /// Returns the number of options added (zero when nothing applies —
    /// no template requested, project not init'd, or template not found).
    /// </summary>
    /// <remarks>
    /// Always drops options added by a previous call first so the
    /// singleton command never accumulates stale entries across
    /// invocations. The hydrator may run async I/O (project resolution,
    /// workload registry lookup) but the help action is sync, so this is
    /// driven via <see cref="Task{T}.GetAwaiter"/> on the work-stealing
    /// thread pool — fine because the renderer is itself called from a
    /// non-UI sync context.
    /// </remarks>
    public int AttachHydratedOptionsForHelp(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        // Drop any options we previously attached so reruns start clean.
        // Comparing the snapshot of built-in names is safer than holding
        // a list of "added" option instances across invocations.
        var stale = Options
            .Where(o => !_builtInOptionNames.Contains(o.Name))
            .ToList();
        foreach (Option o in stale)
        {
            Options.Remove(o);
        }

        string? templateId = parseResult.GetValue(TemplateOption);
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return 0;
        }

        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        var invocation = new NewInvocation(
            workingDirectory,
            RequestedTemplate: templateId,
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);

        IReadOnlyList<Option>? hydrated;
        try
        {
            hydrated = _runner.HydrateOptionsForTemplateAsync(invocation, templateId, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Resolution / I/O failures are non-fatal at help time. Falling
            // back to built-ins-only output is preferable to crashing inside
            // the help renderer.
            return 0;
        }

        if (hydrated is null || hydrated.Count == 0)
        {
            return 0;
        }

        foreach (Option o in hydrated)
        {
            // Skip names that collide with built-ins so we don't shadow
            // them with template-defined duplicates.
            if (_builtInOptionNames.Contains(o.Name))
            {
                continue;
            }

            Options.Add(o);
        }

        return Options.Count - _builtInOptionNames.Count;
    }
}

/// <summary>
/// Marker for commands whose <c>--help</c> output depends on a value bound
/// by an earlier (stage-A) parse. The Spectre help action calls
/// <see cref="AttachHydratedOptionsForHelp"/> on the matched command before
/// rendering so the user sees the union of built-in options and any
/// options the bound value (e.g. <c>--template &lt;id&gt;</c>) implies.
/// </summary>
internal interface ITemplateAwareHelpProvider
{
    public int AttachHydratedOptionsForHelp(ParseResult parseResult);
}
