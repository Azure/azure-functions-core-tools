// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Projects the engine's template cache into the CLI's engine-agnostic
/// <see cref="FunctionTemplateInfo"/> catalog: filters by stack/language,
/// dedupes language variants by group identity, merges <c>func.host.json</c>
/// presentation hints, and excludes constraint-restricted templates. A single
/// malformed template is skipped with a warning rather than failing the scan.
/// </summary>
internal sealed class FuncTemplateCatalog(
    IFuncTemplateEngineSession session,
    IFuncTemplateConstraintEvaluator constraintEvaluator,
    IFuncTemplateMountFileReader mountFileReader,
    ILogger<FuncTemplateCatalog> logger) : IFuncTemplateCatalog
{
    private readonly IFuncTemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly IFuncTemplateConstraintEvaluator _constraintEvaluator = constraintEvaluator ?? throw new ArgumentNullException(nameof(constraintEvaluator));
    private readonly IFuncTemplateMountFileReader _mountFileReader = mountFileReader ?? throw new ArgumentNullException(nameof(mountFileReader));
    private readonly ILogger<FuncTemplateCatalog> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<IReadOnlyList<FunctionTemplateInfo>> ListAsync(TemplateListContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (await ScanAsync(context, cancellationToken)).Available;
    }

    /// <inheritdoc />
    public async Task<RestrictedTemplateInfo?> FindRestrictedAsync(TemplateListContext context, string requestedTemplate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedTemplate);

        string trimmed = requestedTemplate.Trim();
        IReadOnlyList<RestrictedTemplateInfo> restricted = (await ScanAsync(context, cancellationToken)).Restricted;
        return restricted.FirstOrDefault(r =>
            string.Equals(r.Template.Id, trimmed, StringComparison.OrdinalIgnoreCase)
            || r.Template.ShortNames.Any(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<(IReadOnlyList<FunctionTemplateInfo> Available, IReadOnlyList<RestrictedTemplateInfo> Restricted)> ScanAsync(
        TemplateListContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ITemplateInfo> allTemplates = await _session.PackageManager.GetTemplatesAsync(cancellationToken);

        var candidates = new List<ITemplateInfo>();
        foreach (ITemplateInfo template in allTemplates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (template.IsItemTemplate()
                    && template.MatchesStack(context.Stack)
                    && template.MatchesLanguage(context.Language))
                {
                    candidates.Add(template);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSkipped(template, ex);
            }
        }

        IReadOnlyDictionary<string, TemplateConstraintEvaluation> constraints =
            await _constraintEvaluator.EvaluateAsync(candidates, cancellationToken);

        var available = new List<FunctionTemplateInfo>();
        var restricted = new List<RestrictedTemplateInfo>();
        foreach (IGrouping<string, ITemplateInfo> group in GroupByIdentity(candidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ITemplateInfo representative = group.First();
            try
            {
                TemplateConstraintEvaluation evaluation = constraints.TryGetValue(representative.Identity, out TemplateConstraintEvaluation? value)
                    ? value
                    : TemplateConstraintEvaluation.Allowed;
                if (!evaluation.IsAllowed)
                {
                    // Restricted templates are excluded from the selectable set but
                    // still projected with their reason, so an explicit request for
                    // one surfaces the call-to-action instead of "unknown template".
                    _logger.LogDebug(
                        "Excluding restricted template {TemplateIdentity}: {Reason}",
                        representative.Identity,
                        evaluation.ErrorMessage);
                    restricted.Add(new RestrictedTemplateInfo(Project(representative, group), evaluation.ToRestrictionMessage()));
                    continue;
                }

                available.Add(Project(representative, group));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSkipped(representative, ex);
            }
        }

        return (available, restricted);
    }

    private static IEnumerable<IGrouping<string, ITemplateInfo>> GroupByIdentity(IEnumerable<ITemplateInfo> templates)
        => templates.GroupBy(
            t => string.IsNullOrWhiteSpace(t.GroupIdentity) ? t.Identity : t.GroupIdentity!,
            StringComparer.OrdinalIgnoreCase);

    private static bool IsUserPrompt(ITemplateInfo template, ITemplateParameter parameter)
    {
        if (!string.Equals(parameter.Type, "parameter", StringComparison.Ordinal))
        {
            return false;
        }

        if (parameter.IsName || string.Equals(parameter.Name, "name", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !template.TagsCollection.ContainsKey(parameter.Name);
    }

    private static string NormalizeDataType(string? dataType) => dataType?.ToLowerInvariant() switch
    {
        null or "" or "text" or "string" => "string",
        "integer" or "int" => "int",
        "bool" or "boolean" => "bool",
        _ => dataType!.ToLowerInvariant(),
    };

    private static (bool RequiresBundle, string? MinBundleVersion) ReadBundleRequirement(ITemplateInfo template)
    {
        foreach (TemplateConstraintInfo constraint in template.Constraints)
        {
            if (string.Equals(constraint.Type, FuncTemplateTags.ExtensionBundleConstraintType, StringComparison.OrdinalIgnoreCase))
            {
                return (true, TryReadConstraintVersion(constraint.Args));
            }
        }

        return (false, null);
    }

    private static string? TryReadConstraintVersion(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(args);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<TemplateUserPrompt> BuildPrompts(ITemplateInfo template, FuncHostFile hostFile)
    {
        var prompts = new List<TemplateUserPrompt>();
        foreach (ITemplateParameter parameter in template.ParameterDefinitions)
        {
            if (!IsUserPrompt(template, parameter))
            {
                continue;
            }

            FuncHostSymbolInfo? symbol = hostFile.FindSymbol(parameter.Name);
            if (symbol is { IsHidden: true })
            {
                continue;
            }

            prompts.Add(new TemplateUserPrompt(
                parameter.Name,
                parameter.Description ?? parameter.DisplayName,
                NormalizeDataType(parameter.DataType),
                parameter.DefaultValue,
                parameter.Choices?.Keys.ToList(),
                parameter.Precedence.IsRequired,
                symbol?.Validator?.Expression,
                ShortAlias: null,
                symbol?.LongName is { } longName ? "--" + longName : null));
        }

        if (hostFile.FunctionNameValidator is { } nameValidator)
        {
            prompts.Add(new TemplateUserPrompt(
                "name",
                "The function name.",
                "string",
                DefaultValue: null,
                Choices: null,
                IsRequired: false,
                nameValidator.Expression,
                ShortAlias: null,
                LongAlias: null));
        }

        return prompts;
    }

    private FunctionTemplateInfo Project(ITemplateInfo representative, IEnumerable<ITemplateInfo> group)
    {
        string? hostJson = _mountFileReader.TryReadFile(representative, representative.HostConfigPlace);
        FuncHostFile hostFile = FuncHostFileParser.Parse(hostJson);

        string id = representative.ShortNameList.Count > 0 ? representative.ShortNameList[0] : representative.Identity;
        string stack = representative.Tag(FuncTemplateTags.Stack) ?? string.Empty;

        IReadOnlyList<string> languages = group
            .Select(t => t.Tag(FuncTemplateTags.Language))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        (bool requiresBundle, string? minBundleVersion) = ReadBundleRequirement(representative);

        return new FunctionTemplateInfo(
            id,
            stack,
            string.IsNullOrWhiteSpace(representative.Name) ? id : representative.Name,
            representative.Description,
            representative.DefaultName,
            languages,
            new TemplateMetadata(BuildPrompts(representative, hostFile), requiresBundle, minBundleVersion))
        {
            ShortNames = representative.ShortNameList,
        };
    }

    private void LogSkipped(ITemplateInfo template, Exception exception)
    {
        string packageId = string.IsNullOrEmpty(template.MountPointUri)
            ? "unknown"
            : Path.GetFileNameWithoutExtension(template.MountPointUri);
        _logger.LogWarning(
            exception,
            "[{PackageId}] Skipping malformed template '{TemplateIdentity}': {Message}",
            packageId,
            template.Identity,
            exception.Message);
    }
}
