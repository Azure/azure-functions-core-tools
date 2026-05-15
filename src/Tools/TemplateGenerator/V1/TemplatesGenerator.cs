// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1;

[Generator]
public sealed class TemplatesGenerator : IIncrementalGenerator
{
    private const string TemplateVersionKey = "build_metadata.AdditionalFiles.TemplateVersion";
    private const string TemplateLanguageKey = "build_metadata.AdditionalFiles.TemplateLanguage";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Emit POCO types unconditionally so the consuming project always has type definitions.
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddSource("FunctionTemplate.g.cs", TemplateEmitter.EmitFunctionTemplate());
            ctx.AddSource("FunctionDefinition.g.cs", TemplateEmitter.EmitFunctionDefinition());
            ctx.AddSource("BindingDirection.g.cs", TemplateEmitter.EmitBindingDirection());
            ctx.AddSource("TemplateBinding.g.cs", TemplateEmitter.EmitTemplateBinding());
            ctx.AddSource("TemplateMetadata.g.cs", TemplateEmitter.EmitTemplateMetadata());
        });

        // Find AdditionalFiles marked with TemplateVersion="v1", parse, and emit static data.
        // Optionally filter by TemplateLanguage metadata (matches the "-{Language}" suffix on template ids).
        IncrementalValuesProvider<EquatableArray<TemplateModel>> templates = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair =>
            {
                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                return options.TryGetValue(TemplateVersionKey, out string? value)
                    && value.Equals("v1", StringComparison.OrdinalIgnoreCase);
            })
            .Select(static (pair, ct) =>
            {
                string? text = pair.Left.GetText(ct)?.ToString();
                if (text is null)
                {
                    return default;
                }

                AnalyzerConfigOptions options = pair.Right.GetOptions(pair.Left);
                options.TryGetValue(TemplateLanguageKey, out string? language);

                return TemplateParser.Parse(text, language);
            })
            .Where(static t => t.Length > 0);

        context.RegisterSourceOutput(templates, static (ctx, t) =>
        {
            foreach ((string hintName, string source) in TemplateEmitter.EmitStaticData(t))
            {
                ctx.AddSource(hintName, source);
            }
        });
    }
}
