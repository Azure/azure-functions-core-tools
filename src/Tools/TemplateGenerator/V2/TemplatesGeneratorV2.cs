// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Common;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2;

/// <summary>
/// Source generator for v2 templates.json and v2 userPrompts.json.
/// </summary>
[Generator]
public sealed class TemplatesGeneratorV2 : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            // V2 template POCOs.
            ctx.AddSource("FunctionTemplateV2.g.cs", TemplateEmitterV2.EmitFunctionTemplate());
            ctx.AddSource("TemplateJob.g.cs", TemplateEmitterV2.EmitTemplateJob());
            ctx.AddSource("TemplateJobInput.g.cs", TemplateEmitterV2.EmitTemplateJobInput());
            ctx.AddSource("TemplateInputCondition.g.cs", TemplateEmitterV2.EmitTemplateInputCondition());
            ctx.AddSource("TemplateAction.g.cs", TemplateEmitterV2.EmitTemplateAction());

            // V2 user prompt POCOs.
            ctx.AddSource("UserPromptV2.g.cs", UserPromptsEmitterV2.EmitUserPrompt());
            ctx.AddSource("UserPromptValueTypeV2.g.cs", UserPromptsEmitterV2.EmitValueType());
            ctx.AddSource("UserPromptValidatorV2.g.cs", UserPromptsEmitterV2.EmitValidator());
            ctx.AddSource("UserPromptEnumValueV2.g.cs", UserPromptsEmitterV2.EmitEnumValue());
        });

        // Templates pipeline.
        IncrementalValuesProvider<EquatableArray<TemplateModelV2>> templates = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair =>
            {
                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return AdditionalFileMetadata.IsAssetKind(options, AdditionalFileMetadata.AssetKindTemplates)
                    && AdditionalFileMetadata.HasTemplateVersion(options, "v2");
            })
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                if (text is null)
                {
                    return default;
                }

                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return TemplateParserV2.Parse(text, AdditionalFileMetadata.GetTemplateLanguage(options));
            })
            .Where(static t => t.Length > 0);

        context.RegisterSourceOutput(templates, static (ctx, t) =>
        {
            foreach ((string hintName, string source) in TemplateEmitterV2.EmitStaticData(t))
            {
                ctx.AddSource(hintName, source);
            }
        });

        // User prompts pipeline.
        IncrementalValuesProvider<UserPromptsCatalogModelV2> userPrompts = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair =>
            {
                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return AdditionalFileMetadata.IsAssetKind(options, AdditionalFileMetadata.AssetKindUserPrompts)
                    && AdditionalFileMetadata.HasTemplateVersion(options, "v2");
            })
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                return text is null ? null : UserPromptsParserV2.Parse(text);
            })
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(userPrompts, static (ctx, catalog) =>
        {
            foreach ((string hintName, string source) in UserPromptsEmitterV2.EmitStaticData(catalog))
            {
                ctx.AddSource(hintName, source);
            }
        });

        // Resources pipeline (per-culture parse, then collect into a single catalog).
        IncrementalValuesProvider<ResourceCultureModelV2> resourceCultures = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair =>
            {
                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return AdditionalFileMetadata.IsAssetKind(options, AdditionalFileMetadata.AssetKindResources)
                    && AdditionalFileMetadata.HasTemplateVersion(options, "v2");
            })
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                if (text is null)
                {
                    return null;
                }

                string culture = ExtractCultureFromPath(pair.Left.Path);
                return ResourcesParserV2.Parse(text, culture);
            })
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        IncrementalValueProvider<ResourcesCatalogModelV2> resourcesCatalog = resourceCultures.Collect()
            .Select(static (cultures, _) => new ResourcesCatalogModelV2 { Cultures = cultures });

        context.RegisterSourceOutput(resourcesCatalog, static (ctx, catalog) =>
        {
            if (catalog.Cultures.Length == 0)
            {
                return;
            }

            foreach ((string hintName, string source) in ResourcesEmitterV2.EmitStaticData(catalog))
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
