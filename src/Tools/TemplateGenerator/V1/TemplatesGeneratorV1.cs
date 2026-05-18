// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Common;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1;

/// <summary>
/// Source generator for v1 templates.json and v1 bindings.json.
/// </summary>
[Generator]
public sealed class TemplatesGeneratorV1 : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            // V1 template POCOs.
            ctx.AddSource("FunctionTemplateV1.g.cs", TemplateEmitterV1.EmitFunctionTemplate());
            ctx.AddSource("FunctionDefinitionV1.g.cs", TemplateEmitterV1.EmitFunctionDefinition());
            ctx.AddSource("BindingDirectionV1.g.cs", TemplateEmitterV1.EmitBindingDirection());
            ctx.AddSource("TemplateBindingV1.g.cs", TemplateEmitterV1.EmitTemplateBinding());
            ctx.AddSource("TemplateMetadataV1.g.cs", TemplateEmitterV1.EmitTemplateMetadata());

            // V1 binding POCOs.
            ctx.AddSource("BindingDefinitionV1.g.cs", BindingsEmitterV1.EmitBindingDefinition());
            ctx.AddSource("BindingSchemaDirectionV1.g.cs", BindingsEmitterV1.EmitDirection());
            ctx.AddSource("BindingSettingV1.g.cs", BindingsEmitterV1.EmitSetting());
            ctx.AddSource("BindingSettingValueTypeV1.g.cs", BindingsEmitterV1.EmitSettingValueType());
            ctx.AddSource("BindingValueValidatorV1.g.cs", BindingsEmitterV1.EmitValidator());
            ctx.AddSource("BindingEnumValueV1.g.cs", BindingsEmitterV1.EmitEnumValue());
            ctx.AddSource("BindingActionRefV1.g.cs", BindingsEmitterV1.EmitActionRef());
            ctx.AddSource("BindingExtensionRefV1.g.cs", BindingsEmitterV1.EmitExtensionRef());
            ctx.AddSource("BindingRuleV1.g.cs", BindingsEmitterV1.EmitRule());
            ctx.AddSource("BindingRuleValueV1.g.cs", BindingsEmitterV1.EmitRuleValue());
        });

        // Templates pipeline.
        IncrementalValuesProvider<EquatableArray<TemplateModelV1>> templates = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair =>
            {
                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return AdditionalFileMetadata.IsAssetKind(options, AdditionalFileMetadata.AssetKindTemplates)
                    && AdditionalFileMetadata.HasTemplateVersion(options, "v1");
            })
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                if (text is null)
                {
                    return default;
                }

                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return TemplateParserV1.Parse(text, AdditionalFileMetadata.GetTemplateLanguage(options));
            })
            .Where(static t => t.Length > 0);

        context.RegisterSourceOutput(templates, static (ctx, t) =>
        {
            foreach ((string hintName, string source) in TemplateEmitterV1.EmitStaticData(t))
            {
                ctx.AddSource(hintName, source);
            }
        });

        // Bindings pipeline.
        IncrementalValuesProvider<BindingsCatalogModelV1> bindings = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair =>
            {
                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return AdditionalFileMetadata.IsAssetKind(options, AdditionalFileMetadata.AssetKindBindings)
                    && AdditionalFileMetadata.HasTemplateVersion(options, "v1");
            })
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                return text is null ? null : BindingsParserV1.Parse(text);
            })
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(bindings, static (ctx, catalog) =>
        {
            foreach ((string hintName, string source) in BindingsEmitterV1.EmitStaticData(catalog))
            {
                ctx.AddSource(hintName, source);
            }
        });

        // Resources pipeline (per-culture parse, then collect into a single catalog).
        IncrementalValuesProvider<ResourceCultureModelV1> resourceCultures = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair =>
            {
                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return AdditionalFileMetadata.IsAssetKind(options, AdditionalFileMetadata.AssetKindResources)
                    && AdditionalFileMetadata.HasTemplateVersion(options, "v1");
            })
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                if (text is null)
                {
                    return null;
                }

                string culture = ExtractCultureFromPath(pair.Left.Path);
                return ResourcesParserV1.Parse(text, culture);
            })
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        IncrementalValueProvider<ResourcesCatalogModelV1> resourcesCatalog = resourceCultures.Collect()
            .Select(static (cultures, _) => new ResourcesCatalogModelV1 { Cultures = cultures });

        context.RegisterSourceOutput(resourcesCatalog, static (ctx, catalog) =>
        {
            if (catalog.Cultures.Length == 0)
            {
                return;
            }

            foreach ((string hintName, string source) in ResourcesEmitterV1.EmitStaticData(catalog))
            {
                ctx.AddSource(hintName, source);
            }
        });
    }

    /// <summary>
    /// Extracts the culture name from a Resources file path. "Resources.json" → "en";
    /// "Resources.{culture}.json" → "{culture}" (e.g. "Resources.de-DE.json" → "de-DE").
    /// </summary>
    private static string ExtractCultureFromPath(string path)
    {
        string filename = global::System.IO.Path.GetFileName(path);
        const string prefix = "Resources.";
        const string suffix = ".json";
        if (filename.Length <= prefix.Length + suffix.Length
            || !filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        string middle = filename.Substring(prefix.Length, filename.Length - prefix.Length - suffix.Length);
        return string.IsNullOrEmpty(middle) ? "en" : middle;
    }
}
