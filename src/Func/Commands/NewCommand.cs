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

    public Option<string?> OutputOption { get; } = new("--output")
    {
        Description = "Output mode (plain | json). Defaults to plain.",
    };

    private readonly NewCommandRunner _runner;
    private readonly HashSet<string> _builtInOptionNames;
    private readonly Dictionary<string, string> _optionNameToPromptId = new(StringComparer.OrdinalIgnoreCase);

    // Template id whose options are currently attached to this command.
    // Set whenever AttachHydratedOptions* succeeds, cleared in
    // DropDynamicOptions. The help renderer uses it (via
    // GetTemplateHelpInfo) to title the "Template Options (<id>)" section.
    private string? _attachedTemplateId;

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
        Options.Add(OutputOption);

        // Tolerate unrecognised options so ExecuteAsync can re-interpret
        // them as per-template prompt values (e.g. `--auth-level anonymous`
        // for HttpTrigger). Without this SCL would reject the invocation
        // before ExecuteAsync ever runs and the user couldn't supply the
        // hydrated options surfaced under `--help`.
        TreatUnmatchedTokensAsErrors = false;

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
        string? requestedTemplate = parseResult.GetValue(TemplateOption);
        bool jsonOutput = string.Equals(parseResult.GetValue(OutputOption), "json", StringComparison.OrdinalIgnoreCase);

        // Collect per-template prompt overrides. Two sources, in priority order:
        //   1. Options the pre-parse step in Program.Main attached to this
        //      command from the hydrator. SCL parsed them; read the values
        //      out of `parseResult` and translate Option.Name (kebab-cased
        //      paramId) back into the v2 paramId the engine expects.
        //   2. UnmatchedTokens — only populated when pre-parse couldn't
        //      attach the options (template not found, project not init'd,
        //      etc.). Walk the pairs manually so a typo still surfaces a
        //      sensible "Template was not found" rather than a confusing
        //      "Unrecognized option" error.
        IReadOnlyDictionary<string, string?>? userOptionValues = null;
        if (!string.IsNullOrWhiteSpace(requestedTemplate))
        {
            userOptionValues = ReadParsedPromptOverrides(parseResult)
                ?? ParsePromptOverrides(parseResult.UnmatchedTokens, requestedTemplate);
        }

        var invocation = new NewInvocation(
            workingDirectory,
            RequestedTemplate: requestedTemplate,
            RequestedFunctionName: parseResult.GetValue(NameOption),
            Force: parseResult.GetValue(ForceOption),
            NonInteractive: parseResult.GetValue(NonInteractiveOption),
            JsonOutput: jsonOutput,
            UserOptionValues: userOptionValues);

        return parseResult.GetValue(ListOption)
            ? _runner.ListAsync(invocation, cancellationToken)
            : _runner.ExecuteAsync(invocation, cancellationToken);
    }

    /// <summary>
    /// Reads supplied values for the options the pre-parse step attached
    /// to this command. Each hydrated <see cref="Option"/> was registered
    /// with name <c>--&lt;kebab-paramId&gt;</c>; translate back to the v2
    /// paramId so the engine's variable substitution can pick the value
    /// up. Returns <c>null</c> when no hydrated options were attached
    /// (or none were bound on this invocation).
    /// </summary>
    private Dictionary<string, string?>? ReadParsedPromptOverrides(ParseResult parseResult)
    {
        var dynamicOptions = Options.Where(o => !_builtInOptionNames.Contains(o.Name)).ToList();
        if (dynamicOptions.Count == 0)
        {
            return null;
        }

        Dictionary<string, string?>? overrides = null;
        foreach (Option option in dynamicOptions)
        {
            System.CommandLine.Parsing.OptionResult? result = parseResult.GetResult(option);
            if (result is null || result.Tokens.Count == 0)
            {
                continue;
            }

            // The hydrator creates Option<string?>, so reading via Tokens
            // gives us the raw string the user typed without having to
            // know the closed generic type at this layer.
            string raw = result.Tokens[0].Value;
            overrides ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            // Translate the kebab-cased option.Name back to the v2 paramId
            // the engine resolves against. Falls back to the trimmed name
            // if the mapping is missing (defensive — shouldn't happen for
            // options attached via AttachHydratedOptionsForPreParse).
            string promptId = _optionNameToPromptId.TryGetValue(option.Name, out string? mapped)
                ? mapped
                : option.Name.TrimStart('-');
            overrides[promptId] = raw;
        }

        return overrides;
    }

    /// <summary>
    /// Translates SCL's <see cref="ParseResult.UnmatchedTokens"/> for a
    /// <c>func new -t &lt;id&gt; --foo bar --baz qux</c> invocation into a
    /// per-prompt override dict the runner can hand to the engine. The
    /// hydrator is the source of truth for the available prompt set; only
    /// tokens that match a hydrated <see cref="Option"/> survive — unknown
    /// ones are ignored so a typo doesn't silently mask the real default
    /// (SCL's normal error UX for unknown options is suppressed because
    /// <see cref="TreatUnmatchedTokensAsErrors"/> is off; surface the same
    /// posture by simply ignoring them here).
    /// </summary>
    private IReadOnlyDictionary<string, string?>? ParsePromptOverrides(
        IReadOnlyList<string> unmatched,
        string templateId)
    {
        IReadOnlyList<Option>? hydrated;
        try
        {
            var invocation = new NewInvocation(
                new WorkingDirectory(new DirectoryInfo(Environment.CurrentDirectory), false),
                RequestedTemplate: templateId,
                RequestedFunctionName: null,
                Force: false,
                NonInteractive: true);

            hydrated = _runner.HydrateOptionsForTemplateAsync(invocation, templateId, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }

        if (hydrated is null || hydrated.Count == 0)
        {
            return null;
        }

        // Build a name → promptId map so both --kebab-name and the original
        // paramId form land on the right key. The hydrator names each
        // Option after the kebab-cased paramId.
        var nameToPromptId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Option option in hydrated)
        {
            string promptId = option.Name.TrimStart('-');
            nameToPromptId[option.Name] = promptId;
            foreach (string alias in option.Aliases)
            {
                nameToPromptId[alias] = promptId;
            }
        }

        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < unmatched.Count; i++)
        {
            string token = unmatched[i];
            if (!token.StartsWith('-'))
            {
                continue;
            }

            // Tolerate --opt=value syntax in addition to --opt value.
            int eq = token.IndexOf('=');
            if (eq > 0)
            {
                string name = token[..eq];
                string val = token[(eq + 1)..];
                if (nameToPromptId.TryGetValue(name, out string? promptIdInline))
                {
                    overrides[promptIdInline] = val;
                }
                continue;
            }

            if (!nameToPromptId.TryGetValue(token, out string? promptId))
            {
                continue;
            }

            string? value = (i + 1 < unmatched.Count && !unmatched[i + 1].StartsWith('-'))
                ? unmatched[++i]
                : "true";

            overrides[promptId] = value;
        }

        return overrides.Count == 0 ? null : overrides;
    }

    /// <summary>
    /// Pre-parse hook: invoked by <see cref="NewCommandArgPreparer"/>
    /// from <c>Program.Main</c> before the parser runs, so per-template
    /// options the user supplies on the same invocation as
    /// <c>--template &lt;id&gt;</c> get registered in time for SCL to
    /// recognise them. Idempotent — duplicate calls (including the
    /// help-time path) drop previously-attached options first.
    /// </summary>
    public void AttachHydratedOptionsForPreParse(string templateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        DropDynamicOptions();

        var invocation = new NewInvocation(
            new WorkingDirectory(new DirectoryInfo(Environment.CurrentDirectory), false),
            RequestedTemplate: templateId,
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);

        IReadOnlyList<HydratedTemplateOption>? hydrated;
        try
        {
            hydrated = _runner.HydrateOptionsForTemplateWithIdsAsync(invocation, templateId, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch
        {
            return;
        }

        if (hydrated is null || hydrated.Count == 0)
        {
            return;
        }

        foreach (HydratedTemplateOption pair in hydrated)
        {
            if (_builtInOptionNames.Contains(pair.Option.Name))
            {
                continue;
            }

            Options.Add(pair.Option);
            _optionNameToPromptId[pair.Option.Name] = pair.PromptId;
        }

        _attachedTemplateId = templateId;
    }

    private void DropDynamicOptions()
    {
        var stale = Options
            .Where(o => !_builtInOptionNames.Contains(o.Name))
            .ToList();
        foreach (Option o in stale)
        {
            Options.Remove(o);
        }
        _optionNameToPromptId.Clear();
        _attachedTemplateId = null;
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

        // PathArgument's CustomParser short-circuits with a parser error
        // (and a sentinel return value) when the user typed something like
        // `func new -name ttpt`: the `-name` token gets bound to <path>
        // and PathArgument flags it as an unrecognized option. SCL's
        // ArgumentConverter then throws when we call GetValue here, even
        // though help-time hydration only needs a directory to probe.
        // Fall back to cwd so the user still sees the template's option
        // surface alongside the real parse-error diagnostic.
        WorkingDirectory workingDirectory;
        try
        {
            workingDirectory = parseResult.GetValue(PathArgument!)!;
        }
        catch
        {
            workingDirectory = WorkingDirectory.FromCwd();
        }

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

        _attachedTemplateId = templateId;
        return Options.Count - _builtInOptionNames.Count;
    }

    /// <inheritdoc />
    public TemplateHelpInfo? GetTemplateHelpInfo()
    {
        if (string.IsNullOrEmpty(_attachedTemplateId))
        {
            return null;
        }

        // Hydrated options are exactly those on the command whose names
        // aren't in the original built-in snapshot. Using this diff is
        // robust to either attach path (pre-parse or help-time) and stays
        // correct even if the prompt-id map is ever populated lazily.
        var hydrated = Options
            .Where(o => !_builtInOptionNames.Contains(o.Name))
            .ToList();

        return hydrated.Count == 0
            ? null
            : new TemplateHelpInfo(_attachedTemplateId, hydrated);
    }
}

/// <summary>
/// Marker for commands whose <c>--help</c> output depends on a value bound
/// before the main parse. The Spectre help action calls
/// <see cref="AttachHydratedOptionsForHelp"/> on the matched command before
/// rendering so the user sees the union of built-in options and any
/// options the bound value (e.g. <c>--template &lt;id&gt;</c>) implies.
/// <see cref="GetTemplateHelpInfo"/> then lets the renderer separate the
/// hydrated options into their own section.
/// </summary>
internal interface ITemplateAwareHelpProvider
{
    public int AttachHydratedOptionsForHelp(ParseResult parseResult);

    /// <summary>
    /// Returns the template id + the options hydrated for that template,
    /// or <c>null</c> when no template-derived options are currently
    /// attached. Called after <see cref="AttachHydratedOptionsForHelp"/>
    /// so the help renderer can split a "Template Options (&lt;id&gt;)"
    /// section out of the main "Options" section.
    /// </summary>
    public TemplateHelpInfo? GetTemplateHelpInfo();
}

/// <summary>
/// A template id paired with the CLI options the template hydrates.
/// Returned by <see cref="ITemplateAwareHelpProvider.GetTemplateHelpInfo"/>
/// so the help renderer can render those options under their own section
/// heading instead of mixing them with the command's built-in options.
/// </summary>
internal sealed record TemplateHelpInfo(string TemplateId, IReadOnlyList<Option> HydratedOptions);
