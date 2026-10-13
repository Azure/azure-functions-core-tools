// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using Azure.Functions.Cli.Projects;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed record TemplateProjectConfigurationDeclaration(int PrimaryOutputIndex, string Path, string Stack, string Language);

internal sealed class TemplateProjectConfigurationPlanner
{
    internal const string ConfigurationActionId = "00e33184-ffc7-43ef-bec0-b1684df8ad56";
    private readonly IReadOnlyList<StackDefinition> _stacks;
    private readonly StringComparer _pathComparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public TemplateProjectConfigurationPlanner(IEnumerable<IProjectInitializer> initializers)
    {
        ArgumentNullException.ThrowIfNull(initializers);
        _stacks = Array.AsReadOnly<StackDefinition>([.. initializers.Select(initializer => new StackDefinition(initializer.Stack,
            Array.AsReadOnly<string>([.. initializer.SupportedLanguages]),
            initializer.SupportedLanguageAliases.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)Array.AsReadOnly<string>([.. pair.Value]), StringComparer.Ordinal)))]);
    }

    public IReadOnlyList<TemplateProjectConfigurationDeclaration> Read(string configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        try
        {
            using var document = JsonDocument.Parse(configuration, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonElement root = document.RootElement;
            Require(root.ValueKind == JsonValueKind.Object, "Project template configuration must be an object.");
            RejectDuplicates(root);
            foreach (string property in new[] { "sources", "specialCustomOperations", "globalCustomOperations", "baselines", "additionalConfigFiles" })
            {
                Require(!root.EnumerateObject().Any(candidate => string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase)),
                    $"Project topology property '{property}' is not qualified for this creation path.");
            }

            Require(root.EnumerateObject().All(property => !string.Equals(property.Name, "symbols", StringComparison.OrdinalIgnoreCase) || property.Name == "symbols"),
                "Project symbol metadata must use the canonical symbols property name.");
            if (root.TryGetProperty("symbols", out JsonElement symbols))
            {
                Require(symbols.ValueKind == JsonValueKind.Object, "Project symbols must be an object.");
                foreach (JsonProperty symbol in symbols.EnumerateObject())
                {
                    Require(symbol.Value.ValueKind == JsonValueKind.Object
                        && !symbol.Value.EnumerateObject().Any(property => string.Equals(property.Name, "fileRename", StringComparison.OrdinalIgnoreCase)),
                        "Symbol-driven filename changes are not qualified for project configuration.");
                }
            }

            Require(root.TryGetProperty("constraints", out JsonElement constraints) && constraints.ValueKind == JsonValueKind.Object
                && constraints.EnumerateObject().Any(IsFuncOnlyHostConstraint), "Project configuration actions require func-only host compatibility.");
            Require(root.TryGetProperty("primaryOutputs", out JsonElement outputs) && outputs.ValueKind == JsonValueKind.Array
                && outputs.GetArrayLength() > 0, "Project configuration requires declared primary outputs.");
            string? sourceName = root.TryGetProperty("sourceName", out JsonElement source) ? ReadString(source, "sourceName") : null;
            List<string> paths = [];
            foreach (JsonElement output in outputs.EnumerateArray())
            {
                Require(output.ValueKind == JsonValueKind.Object && output.EnumerateObject().All(property => property.Name == "path"),
                    "Only unconditional literal primary outputs are qualified for project configuration.");
                Require(output.TryGetProperty("path", out JsonElement pathValue), "Each primary output requires a path.");
                string path = ReadString(pathValue, "primary output path");
                Require(!Path.IsPathRooted(path) && !path.Split('/', '\\').Any(segment => segment == ".."), "Primary outputs must remain inside the invocation directory.");
                Require(sourceName is null || !path.Contains(sourceName, StringComparison.OrdinalIgnoreCase), "Renamed primary-output paths require further mapping qualification.");
                paths.Add(path);
            }

            Require(root.TryGetProperty("postActions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array
                && actions.GetArrayLength() > 0, "Every project requires a mandatory configuration action.");
            List<TemplateProjectConfigurationDeclaration> declarations = [];
            foreach (JsonElement action in actions.EnumerateArray())
            {
                Require(action.ValueKind == JsonValueKind.Object
                    && action.EnumerateObject().All(property => property.Name is "actionId" or "args" or "continueOnError" or "description"),
                    "Conditional or unsupported project actions are not qualified.");
                Require(action.TryGetProperty("actionId", out JsonElement id)
                    && Guid.TryParse(ReadString(id, "actionId"), out Guid actionId) && actionId == Guid.Parse(ConfigurationActionId),
                    "This project path supports only mandatory CLI configuration actions.");
                Require(!action.TryGetProperty("continueOnError", out JsonElement continued) || continued.ValueKind == JsonValueKind.False,
                    "Mandatory project configuration cannot continue on error.");
                Require(action.TryGetProperty("args", out JsonElement args) && args.ValueKind == JsonValueKind.Object
                    && args.EnumerateObject().Count() == 3 && args.EnumerateObject().All(property => property.Name is "primaryOutputIndex" or "stack" or "language"),
                    "Configuration arguments must be exactly primaryOutputIndex, stack and language.");
                string indexValue = ReadString(args.GetProperty("primaryOutputIndex"), "primaryOutputIndex");
                Require(int.TryParse(indexValue, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < paths.Count,
                    "primaryOutputIndex must reference an authored primary output using a nonnegative invariant integer string.");
                string stackValue = ReadString(args.GetProperty("stack"), "stack");
                string languageValue = ReadString(args.GetProperty("language"), "language");
                StackDefinition[] stacks = [.. _stacks.Where(stack => string.Equals(stack.Name, stackValue, StringComparison.OrdinalIgnoreCase))];
                Require(stacks.Length == 1, "Configuration stack must identify exactly one installed stack.");
                StackDefinition stack = stacks[0];
                string[] languages = [.. stack.Languages.Where(language => string.Equals(language, languageValue, StringComparison.OrdinalIgnoreCase)
                    || (stack.Aliases.TryGetValue(language, out IReadOnlyList<string>? aliases) && aliases.Any(alias => string.Equals(alias, languageValue, StringComparison.OrdinalIgnoreCase))))];
                Require(languages.Length == 1, "Configuration language must identify exactly one supported stack language.");
                Require(_pathComparer.Equals(Path.GetFileName(paths[index]), "host.json"),
                    "The bounded project path requires configuration to reference host.json.");
                declarations.Add(new(index, paths[index], stack.Name, languages[0]));
            }

            return declarations.AsReadOnly();
        }
        catch (JsonException exception)
        {
            throw new InvalidTemplateMetadataException($"Project configuration metadata is invalid: {exception.Message}");
        }
    }

    public IReadOnlyList<TemplateProjectConfigurationPlan> CreatePlans(string configuration, ICreationEffects effects, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        IReadOnlyList<TemplateProjectConfigurationDeclaration> declarations = Read(configuration);
        string[] primaryOutputs = [.. effects.CreationResult.PrimaryOutputs.Select(output => Path.GetFullPath(Path.Combine(outputDirectory, output.Path)))];
        string[] changedOutputs = [.. effects.FileChanges.Select(change => Path.GetFullPath(Path.Combine(outputDirectory, change.TargetRelativePath)))];
        Require(!changedOutputs.Any(path => string.Equals(Path.GetFileName(path), "config.json", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), ".func", StringComparison.OrdinalIgnoreCase)),
            "Template content must not write CLI-owned project configuration.");
        string[] roots = [.. primaryOutputs.Concat(changedOutputs).Where(path => _pathComparer.Equals(Path.GetFileName(path), "host.json"))
            .Select(path => Path.GetDirectoryName(path)!).Distinct(_pathComparer)];
        Require(roots.Length > 0, "Project creation must produce an active host.json root.");
        List<TemplateProjectConfigurationPlan> plans = [];
        foreach (TemplateProjectConfigurationDeclaration declaration in declarations)
        {
            string path = Path.GetFullPath(Path.Combine(outputDirectory, declaration.Path));
            Require(primaryOutputs.Count(output => _pathComparer.Equals(output, path)) == 1
                && changedOutputs.Contains(path, _pathComparer), "Configuration references must bind to one resolved, generated primary output.");
            plans.Add(new(path, declaration.Stack, declaration.Language));
        }

        Require(plans.Count == roots.Length && roots.All(root => plans.Count(plan => _pathComparer.Equals(plan.Root, root)) == 1),
            "Every independently discovered host.json root must have exactly one mandatory configuration action.");
        Require(plans.All(plan => !changedOutputs.Any(path => IsWithin(path, plan.ConfigurationPath) || IsWithin(plan.ConfigurationPath, path))),
            "Template file effects collide with mandatory project configuration or its directory ancestors.");
        return plans.AsReadOnly();
    }

    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsFuncOnlyHostConstraint(JsonProperty property)
    {
        JsonElement value = property.Value;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String && DecodeString(type) == "host"
            && value.TryGetProperty("args", out JsonElement args) && args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0
            && args.EnumerateArray().All(host => host.ValueKind == JsonValueKind.Object && host.TryGetProperty("hostname", out JsonElement name)
                && name.ValueKind == JsonValueKind.String && DecodeString(name) == "func");
    }

    private static string ReadString(JsonElement value, string name)
    {
        Require(value.ValueKind == JsonValueKind.String, $"'{name}' must be a nonempty raw string.");
        string? decoded = DecodeString(value);
        Require(!string.IsNullOrWhiteSpace(decoded), $"'{name}' must be a nonempty raw string.");
        return decoded!;
    }

    private static string? DecodeString(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidTemplateMetadataException($"Project configuration contains invalid Unicode text: {exception.Message}");
        }
    }

    private static string DecodePropertyName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidTemplateMetadataException($"Project configuration contains an invalid Unicode property name: {exception.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidTemplateMetadataException(message);
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string name = DecodePropertyName(property);
                Require(names.Add(name), $"Duplicate project configuration property '{name}'.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                RejectDuplicates(item);
            }
        }
    }

    private sealed record StackDefinition(string Name, IReadOnlyList<string> Languages, IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases);
}