// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// V2 engine provider. Walks installed
/// <c>Workloads.Templates.&lt;stack&gt;</c> rows, reads their v2 payload, and
/// projects each <see cref="NewTemplate"/> into a
/// <see cref="FunctionTemplateInfo"/>. Apply dispatches to
/// <see cref="V2TemplateEngine"/>.
/// </summary>
/// <remarks>
/// PR2 takes the highest-version installed pkg per stack as a best-effort
/// default and supplies the engine with each prompt's declared default
/// value; PR4 replaces this with the orchestrator's stage-B hydrated parse
/// result so user-supplied option values flow through.
/// </remarks>
internal sealed class V2EngineProvider : ITemplateEngineProvider
{
    private readonly IInstalledTemplatesWorkloads _installed;
    private readonly V2TemplateEngine _engine;

    public V2EngineProvider(IInstalledTemplatesWorkloads installed)
    {
        _installed = installed ?? throw new ArgumentNullException(nameof(installed));
        _engine = new V2TemplateEngine();
    }

    public string EngineId => EngineIds.V2;

    public async Task<IReadOnlyList<FunctionTemplateInfo>> ListTemplatesAsync(
        TemplateListContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? installDir = context.InstallDirectory;
        if (installDir is null)
        {
            // Fallback for callers that invoke the provider directly
            // (e.g. unit tests with no orchestrator). Pick the highest
            // installed version across all channels — channel matching
            // is the orchestrator's responsibility (§4.8.1).
            IReadOnlyList<InstalledTemplatesWorkload> rows = await _installed.ListInstalledAsync(context.Stack, cancellationToken);
            if (rows.Count == 0)
            {
                return [];
            }

            installDir = rows
                .OrderByDescending(r => r.PackageVersion, StringComparer.Ordinal)
                .First()
                .InstallDirectory;
        }

        V2Payload? payload = V2PayloadReader.Load(installDir);
        if (payload is null)
        {
            return [];
        }

        List<FunctionTemplateInfo> projected = [];
        foreach (NewTemplate template in payload.Templates)
        {
            FunctionTemplateInfo? info = V2TemplateProjection.Project(template, payload, context.Stack);
            if (info is not null && MatchesLanguage(info, context.Language))
            {
                projected.Add(info);
            }
        }

        return projected;
    }

    public async Task<TemplateApplicationResult> ApplyAsync(
        NewContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);

        string? installDir = context.InstallDirectory;
        if (installDir is null)
        {
            // Same fallback as ListTemplatesAsync — used only when the
            // provider is invoked outside the orchestrator.
            IReadOnlyList<InstalledTemplatesWorkload> rows = await _installed.ListInstalledAsync(context.Template.Stack, cancellationToken);
            if (rows.Count == 0)
            {
                return new TemplateApplicationResult.Failed(
                    new TemplateApplicationFailure.ProviderError(
                        $"No installed templates workload found for stack '{context.Template.Stack}'.", null));
            }

            installDir = rows
                .OrderByDescending(r => r.PackageVersion, StringComparer.Ordinal)
                .First()
                .InstallDirectory;
        }

        V2Payload? payload = V2PayloadReader.Load(installDir);
        NewTemplate? raw = payload?.Templates.FirstOrDefault(t =>
            string.Equals(t.Id, context.Template.Id, StringComparison.OrdinalIgnoreCase));

        if (payload is null || raw is null)
        {
            return new TemplateApplicationResult.Failed(
                new TemplateApplicationFailure.ProviderError(
                    $"Template '{context.Template.Id}' was not found in the v2 payload at '{installDir}'.",
                    null));
        }

        // PR4: seed defaults from each declared prompt, then override the
        // function-name prompt with the user-supplied context.FunctionName
        // so it wins over the template's declared default. The engine reads
        // values by paramId; the recognised function-name prompt ids match
        // the conventions Node v4 / Python v2 bundle templates use.
        var optionValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateUserPrompt prompt in context.Template.Metadata.UserPrompts)
        {
            optionValues[prompt.Id] = _functionNamePromptIds.Contains(prompt.Id)
                ? context.FunctionName
                : prompt.DefaultValue;
        }

        return _engine.Apply(
            raw,
            context.FunctionName,
            optionValues,
            context.WorkingDirectory.Info,
            context.Force);
    }

    private static readonly HashSet<string> _functionNamePromptIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "functionName",
        "function-name",
        "trigger-functionName",
        "trigger-functionname",
    };

    private static bool MatchesLanguage(FunctionTemplateInfo info, string? requestedLanguage)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguage))
        {
            return true;
        }

        if (info.Languages.Count == 0)
        {
            return true;
        }

        return info.Languages.Any(l =>
            string.Equals(l, requestedLanguage, StringComparison.OrdinalIgnoreCase));
    }
}
