// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Projects installed engine templates into func-owned catalog entries and validates required CLI metadata.
/// </summary>
internal static class TemplateCatalogProjector
{
    private const string TypeTag = "type";
    private const string LanguageTag = "language";
    private const string ItemType = "item";
    private const string ProjectType = "project";

    /// <summary>
    /// Returns the type <paramref name="template"/> declares in <c>tags.type</c>, or <c>null</c> when it's missing or
    /// isn't <c>item</c> or <c>project</c>.
    /// </summary>
    public static TemplateType? GetTemplateType(ITemplateMetadata template)
    {
        ArgumentNullException.ThrowIfNull(template);

        // Authors can write the type in any case, as the engine's own type filter allows.
        return GetTag(template, TypeTag) switch
        {
            string type when type.Equals(ItemType, StringComparison.OrdinalIgnoreCase) => TemplateType.Item,
            string type when type.Equals(ProjectType, StringComparison.OrdinalIgnoreCase) => TemplateType.Project,
            _ => null,
        };
    }

    /// <summary>
    /// Projects <paramref name="template"/> into a catalog entry, reporting unusable Functions or required template metadata.
    /// </summary>
    /// <param name="template">Installed template with an item or project type.</param>
    /// <param name="package">
    /// Package that provides the template, or <c>null</c> when it isn't known. Only managed packages have details.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="template"/> isn't an item or project template.</exception>
    public static TemplateCatalogEntry Project(ITemplateInfo template, ITemplatePackage? package)
    {
        TemplateType type = GetTemplateType(template)
            ?? throw new ArgumentException($"Template '{template.Identity}' isn't an item or project template.", nameof(template));

        FuncHostTemplateMetadata metadata;
        IReadOnlyList<TemplateParameterDefinition> parameters;
        IReadOnlyList<string> shortNames = [.. template.ShortNameList.Where(shortName => !string.IsNullOrWhiteSpace(shortName))];
        string? metadataDiagnostic = null;
        Exception? metadataDiagnosticCause = null;
        try
        {
            metadata = FuncHostTemplateMetadataReader.Read(template);
            parameters = TemplateParameterProjector.Project(template.ParameterDefinitions, metadata);
        }
        catch (InvalidTemplateMetadataException ex)
        {
            // Keep the template listed, with none of its func host metadata applied, so commands can explain why it can't run.
            metadata = FuncHostTemplateMetadata.Empty;
            parameters = [];
            metadataDiagnostic = ex.Message;
            metadataDiagnosticCause = ex;
        }

        if (shortNames.Count == 0)
        {
            const string shortNameDiagnostic = "Template metadata must declare at least one non-empty shortName.";
            metadataDiagnostic = metadataDiagnostic is null
                ? shortNameDiagnostic
                : $"{metadataDiagnostic} {shortNameDiagnostic}";
        }

        return new(
            template.Identity,
            template.Name,
            NullIfEmpty(template.Description),
            type,
            shortNames,
            NullIfEmpty(template.GroupIdentity),
            NullIfEmpty(GetTag(template, LanguageTag)),
            template.Precedence,
            package is IManagedTemplatePackage managed ? new(managed.Identifier, NullIfEmpty(managed.Version)) : null,
            metadata.IsHidden,
            parameters,
            metadataDiagnostic,
            metadataDiagnosticCause);
    }

    private static string? GetTag(ITemplateMetadata template, string name)
        => template.TagsCollection.TryGetValue(name, out string? value) ? value : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
