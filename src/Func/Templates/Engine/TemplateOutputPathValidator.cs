// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TemplateEngine.Core;
using Microsoft.TemplateEngine.Core.Expressions.Cpp2;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.ConfigModel;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.ValueForms;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Qualifies parameter-only output topology before the native engine reevaluates symbols during creation.
/// </summary>
internal static class TemplateOutputPathValidator
{
    private const string DefaultGuidFormats = "ndbpxNDPBX";

    public static string? GetDiagnostic(string configuration, IReadOnlyDictionary<string, object>? populatedParameters = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(configuration));
        var model = TemplateConfigModel.FromStream(stream);
        BaseSymbol[] symbols = [.. model.Symbols];
        HashSet<string> parameters = new(symbols.OfType<ParameterSymbol>().Select(symbol => symbol.Name), StringComparer.Ordinal);
        HashSet<string> unstable = new(symbols.Where(symbol => symbol is not ParameterSymbol).Select(symbol => symbol.Name), StringComparer.Ordinal);

        List<(string Name, string Formats)> guidProducers = [];
        foreach (GeneratedSymbol symbol in symbols.OfType<GeneratedSymbol>().Where(symbol => symbol.Generator == "guid"))
        {
            string formats = DefaultGuidFormats;
            if (symbol.Parameters.TryGetValue("format", out string? format))
            {
                using var document = JsonDocument.Parse(format);
                if (document.RootElement.ValueKind == JsonValueKind.String)
                {
                    string? configured = document.RootElement.GetString();
                    if (!string.IsNullOrEmpty(configured))
                    {
                        formats = configured;
                    }
                }
                else if (document.RootElement.ValueKind != JsonValueKind.Null)
                {
                    return "Template output paths cannot be qualified because a GUID format is not a string.";
                }
            }

            guidProducers.Add((symbol.Name, formats));
        }

        // Only GUID producers write format aliases; other content generators don't overwrite suffix-named parameters.
        for (int index = 0; index < model.Guids.Count; index++)
        {
            string name = $"guid{index}";
            unstable.Add(name);
            guidProducers.Add((name, DefaultGuidFormats));
        }

        foreach ((string name, string formats) in guidProducers)
        {
            foreach (char format in formats)
            {
                unstable.Add($"{name}-{format}");
                unstable.Add($"{name}-{(char.IsUpper(format) ? "uc" : "lc")}-{format}");
            }
        }

        // Form aliases can overwrite declared parameters and feed further forms in any declaration order.
        bool added;
        do
        {
            added = false;
            foreach (BaseValueSymbol symbol in symbols.OfType<BaseValueSymbol>())
            {
                if (!unstable.Contains(symbol.Name))
                {
                    continue;
                }

                foreach (string form in symbol.Forms.GlobalForms)
                {
                    added |= unstable.Add($"{symbol.Name}{{-VALUE-FORMS-}}{form}");
                }
            }
        }
        while (added);

        parameters.ExceptWith(unstable);
        if ((!string.IsNullOrEmpty(model.SourceName) && !parameters.Contains("name"))
            || symbols.OfType<BaseReplaceSymbol>().Any(symbol => !string.IsNullOrWhiteSpace(symbol.FileRename)
                && (symbol is not ParameterSymbol parameter || !parameters.Contains(parameter.Name)
                    || parameter.Forms.GlobalForms.Any(form => unstable.Contains($"{parameter.Name}{{-VALUE-FORMS-}}{form}")))))
        {
            return "Template output paths must use parameter symbols, name, or sourceName. Non-parameter file renames require a frozen native output plan and are not supported.";
        }

        // Native token matching sees only populated, enabled parameter values, not every declared name.
        VariableCollection variables = [];
        foreach (ParameterSymbol parameter in symbols.OfType<ParameterSymbol>().Where(parameter => parameters.Contains(parameter.Name)))
        {
            if (populatedParameters is null || !populatedParameters.TryGetValue(parameter.Name, out object? value) || value is null)
            {
                continue;
            }

            variables[parameter.Name] = value;
            foreach (string form in parameter.Forms.GlobalForms)
            {
                if (value.ToString() is string text && model.Forms.TryGetValue(form, out IValueForm? transformation)
                    && transformation.Process(text, model.Forms) is { } transformed)
                {
                    variables[$"{parameter.Name}{{-VALUE-FORMS-}}{form}"] = transformed;
                }
            }
        }

        foreach (string variable in unstable)
        {
            variables[variable] = false;
        }

        foreach (ExtendedFileSource source in model.Sources)
        {
            if (!HasStableCondition(source.Condition, variables, unstable)
                || source.Modifiers.Any(modifier => !HasStableCondition(modifier.Condition, variables, unstable)))
            {
                return "Template output paths use a source or modifier condition with non-parameter symbols or an unsupported expression. Use parameter-only conditions until a frozen native output plan is supported.";
            }
        }

        return null;
    }

    private static bool HasStableCondition(string? condition, VariableCollection variables, HashSet<string> unstable)
    {
        if (string.IsNullOrEmpty(condition))
        {
            return true;
        }

        // Build, don't evaluate: dependency discovery must include short-circuited branches.
        HashSet<string> references = new(StringComparer.Ordinal);
        return Cpp2StyleEvaluatorDefinition.GetEvaluableExpression(NullLogger.Instance, condition, variables, out string? error, references) is not null
            && string.IsNullOrEmpty(error) && !references.Overlaps(unstable);
    }
}
