// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Projects a <see cref="DotNetTemplateRecord"/> from
/// <c>dotnet-templates.json</c> into the engine-agnostic
/// <see cref="FunctionTemplateInfo"/> the orchestrator consumes.
/// Implements the §5.5.2 / §5.5.4 <c>groupIdentity</c> dedup: when the same
/// catalog ships C# and F# variants of one template (shared
/// <c>groupIdentity</c>), the user sees one row whose
/// <see cref="FunctionTemplateInfo.Languages"/> lists both languages.
/// </summary>
internal static class DotNetTemplateProjection
{
    public static IReadOnlyList<FunctionTemplateInfo> ProjectAll(DotNetTemplatesIndex index, string stack)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (index.Templates is null || index.Templates.Count == 0)
        {
            return [];
        }

        // Group by groupIdentity (case-insensitive); records with no
        // groupIdentity collapse on identity / shortName instead.
        IEnumerable<IGrouping<string, DotNetTemplateRecord>> groups = index.Templates
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .GroupBy(DedupKey, StringComparer.OrdinalIgnoreCase);

        List<FunctionTemplateInfo> results = [];
        foreach (IGrouping<string, DotNetTemplateRecord> group in groups)
        {
            FunctionTemplateInfo? projected = ProjectGroup(group, stack);
            if (projected is not null)
            {
                results.Add(projected);
            }
        }

        return results;
    }

    private static string DedupKey(DotNetTemplateRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.GroupIdentity))
        {
            return record.GroupIdentity;
        }

        if (!string.IsNullOrWhiteSpace(record.Identity))
        {
            return record.Identity;
        }

        return record.Id ?? string.Empty;
    }

    private static FunctionTemplateInfo? ProjectGroup(IEnumerable<DotNetTemplateRecord> group, string stack)
    {
        // Use the first record as the representative for naming + parameters.
        // Languages aggregate across all records in the group.
        var records = group.ToList();
        DotNetTemplateRecord head = records[0];
        if (string.IsNullOrWhiteSpace(head.Id))
        {
            return null;
        }

        List<string> languages = [.. records
            .Where(r => !string.IsNullOrWhiteSpace(r.Language))
            .Select(r => r.Language!.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.Ordinal)];

        TemplateMetadata metadata = new(
            UserPrompts: head.Parameters?.Where(p => !p.IsHidden).Select(ProjectParameter).ToList() ?? [],
            RequiresExtensionBundle: false,
            MinBundleVersion: null);

        return new FunctionTemplateInfo(
            Id: head.Id,
            Stack: stack,
            EngineId: EngineIds.DotNet,
            DisplayName: head.Name ?? head.Id,
            Description: head.Description,
            DefaultFunctionName: head.DefaultName,
            Languages: languages,
            Metadata: metadata);
    }

    private static TemplateUserPrompt ProjectParameter(DotNetParameter parameter)
    {
        string id = parameter.Name ?? string.Empty;
        string dataType = (parameter.DataType ?? "string").ToLowerInvariant();
        IReadOnlyList<string>? choices = parameter.Choices?
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => c.Value!)
            .ToList();

        return new TemplateUserPrompt(
            Id: id,
            Description: parameter.Description ?? parameter.DisplayName,
            DataType: dataType == "choice" ? "choice" : dataType,
            DefaultValue: parameter.DefaultValue,
            Choices: choices,
            IsRequired: parameter.IsRequired,
            ValidatorRegex: null,
            ShortAlias: parameter.ShortNameOverride is { Length: > 0 } s ? "-" + s : null,
            LongAlias: parameter.LongNameOverride is { Length: > 0 } l ? "--" + l : null);
    }
}
