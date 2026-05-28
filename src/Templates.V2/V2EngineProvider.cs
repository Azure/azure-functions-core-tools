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
/// Channel match and min-bundle gates live in the orchestrator (PR4); this
/// provider operates over whichever installed workload(s) the orchestrator
/// hands it. PR2 takes the highest-version installed pkg per stack as a
/// best-effort default and supplies the engine with each prompt's declared
/// default value; PR4 replaces this with the orchestrator's stage-B
/// hydrated parse result so user-supplied option values flow through.
/// </remarks>
public sealed class V2EngineProvider : ITemplateEngineProvider
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

        IReadOnlyList<InstalledTemplatesWorkload> rows = await _installed.ListInstalledAsync(context.Stack, cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        InstalledTemplatesWorkload selected = rows
            .OrderByDescending(r => r.PackageVersion, StringComparer.Ordinal)
            .First();

        V2Payload? payload = V2PayloadReader.Load(selected.InstallDirectory);
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

        IReadOnlyList<InstalledTemplatesWorkload> rows = await _installed.ListInstalledAsync(context.Template.Stack, cancellationToken);
        if (rows.Count == 0)
        {
            return new TemplateApplicationResult.Failed(
                new TemplateApplicationFailure.ProviderError(
                    $"No installed templates workload found for stack '{context.Template.Stack}'.", null));
        }

        InstalledTemplatesWorkload selected = rows
            .OrderByDescending(r => r.PackageVersion, StringComparer.Ordinal)
            .First();

        V2Payload? payload = V2PayloadReader.Load(selected.InstallDirectory);
        NewTemplate? raw = payload?.Templates.FirstOrDefault(t =>
            string.Equals(t.Id, context.Template.Id, StringComparison.OrdinalIgnoreCase));

        if (payload is null || raw is null)
        {
            return new TemplateApplicationResult.Failed(
                new TemplateApplicationFailure.ProviderError(
                    $"Template '{context.Template.Id}' was not found in the v2 payload at '{selected.InstallDirectory}'.",
                    null));
        }

        // PR2: seed defaults from each declared prompt. PR4 replaces this
        // with the orchestrator's stage-B parsed values so user-supplied
        // option values override the prompt defaults.
        var optionValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateUserPrompt prompt in context.Template.Metadata.UserPrompts)
        {
            optionValues[prompt.Id] = prompt.DefaultValue;
        }

        return _engine.Apply(
            raw,
            context.FunctionName,
            optionValues,
            context.WorkingDirectory.Info,
            context.Force);
    }

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
