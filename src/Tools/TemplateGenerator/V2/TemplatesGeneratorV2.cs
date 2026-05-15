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
    }
}
