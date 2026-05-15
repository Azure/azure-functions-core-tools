// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model.V1;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model.V2;
using Azure.Functions.Cli.Tools.TemplateGenerator.V1;
using Azure.Functions.Cli.Tools.TemplateGenerator.V2;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Azure.Functions.Cli.Tools.TemplateGenerator;

[Generator]
public sealed class TemplatesGenerator : IIncrementalGenerator
{
    private const string TemplateVersionKey = "build_metadata.AdditionalFiles.TemplateVersion";
    private const string TemplateLanguageKey = "build_metadata.AdditionalFiles.TemplateLanguage";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Emit POCO types for both v1 and v2 unconditionally so the consuming
        // project always has type definitions, even if it ships only one schema.
        context.RegisterPostInitializationOutput(static ctx =>
        {
            // V1
            ctx.AddSource("FunctionTemplateV1.g.cs", TemplateEmitterV1.EmitFunctionTemplate());
            ctx.AddSource("FunctionDefinitionV1.g.cs", TemplateEmitterV1.EmitFunctionDefinition());
            ctx.AddSource("BindingDirectionV1.g.cs", TemplateEmitterV1.EmitBindingDirection());
            ctx.AddSource("TemplateBindingV1.g.cs", TemplateEmitterV1.EmitTemplateBinding());
            ctx.AddSource("TemplateMetadataV1.g.cs", TemplateEmitterV1.EmitTemplateMetadata());

            // V2
            ctx.AddSource("FunctionTemplateV2.g.cs", TemplateEmitterV2.EmitFunctionTemplate());
            ctx.AddSource("TemplateJob.g.cs", TemplateEmitterV2.EmitTemplateJob());
            ctx.AddSource("TemplateJobInput.g.cs", TemplateEmitterV2.EmitTemplateJobInput());
            ctx.AddSource("TemplateInputCondition.g.cs", TemplateEmitterV2.EmitTemplateInputCondition());
            ctx.AddSource("TemplateAction.g.cs", TemplateEmitterV2.EmitTemplateAction());
        });

        // V1 pipeline: AdditionalFiles where TemplateVersion="v1".
        IncrementalValuesProvider<EquatableArray<TemplateModelV1>> v1Templates = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair => HasTemplateVersion(pair.Right.GetOptions(pair.Left), "v1"))
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                if (text is null)
                {
                    return default;
                }

                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                options.TryGetValue(TemplateLanguageKey, out string? language);

                return TemplateParserV1.Parse(text, language);
            })
            .Where(static t => t.Length > 0);

        context.RegisterSourceOutput(v1Templates, static (ctx, t) =>
        {
            foreach ((string hintName, string source) in TemplateEmitterV1.EmitStaticData(t))
            {
                ctx.AddSource(hintName, source);
            }
        });

        // V2 pipeline: AdditionalFiles where TemplateVersion="v2".
        IncrementalValuesProvider<EquatableArray<TemplateModelV2>> v2Templates = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair => HasTemplateVersion(pair.Right.GetOptions(pair.Left), "v2"))
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                if (text is null)
                {
                    return default;
                }

                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                options.TryGetValue(TemplateLanguageKey, out string? language);

                return TemplateParserV2.Parse(text, language);
            })
            .Where(static t => t.Length > 0);

        context.RegisterSourceOutput(v2Templates, static (ctx, t) =>
        {
            foreach ((string hintName, string source) in TemplateEmitterV2.EmitStaticData(t))
            {
                ctx.AddSource(hintName, source);
            }
        });
    }

    private static bool HasTemplateVersion(AnalyzerConfigOptions options, string expected)
        => options.TryGetValue(TemplateVersionKey, out string? value)
            && value.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
