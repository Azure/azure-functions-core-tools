// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Qualifies the .NET compatibility workflow's pinned project layout and captured native mappings.
/// </summary>
internal static class DotNetProjectLayout
{
    internal static NativeProjectLayoutQualification Qualify(string identity, string fingerprint, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (language is not ("C#" or "F#"))
        {
            throw new ArgumentException("The .NET compatibility layout requires C# or F#.", nameof(language));
        }
        NativeProjectLayoutQualification qualification = new(identity, fingerprint, new(0, "dotnet", language));
        return qualification with
        {
            ValidateConfiguration = configuration => Read(configuration, qualification),
            ValidateCapture = (configuration, effects, output, parameters) => ValidateCapture(configuration, effects, output, qualification, parameters),
        };
    }

    internal static IReadOnlyList<TemplateProjectConfigurationDeclaration> Read(
        TemplateConfigurationSnapshot configuration, NativeProjectLayoutQualification qualification)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(qualification);
        Require(string.Equals(configuration.Fingerprint, qualification.ConfigurationFingerprint, StringComparison.OrdinalIgnoreCase),
            "The selected .NET project configuration does not match its qualified source fingerprint.");
        JsonElement root = configuration.Root;
        Require(root.ValueKind == JsonValueKind.Object, "Project template configuration must be an object.");
        RejectDuplicates(root);
        Require(root.TryGetProperty("identity", out JsonElement identity) && ReadString(identity, "identity") == qualification.TemplateIdentity,
            "The selected .NET project identity does not match its layout qualification.");
        ValidateTopology(root);
        Require(root.EnumerateObject().All(property =>
            !string.Equals(property.Name, "symbols", StringComparison.OrdinalIgnoreCase) || property.Name == "symbols"),
            "Project symbol metadata must use the canonical symbols property name.");
        Require(root.TryGetProperty("symbols", out JsonElement symbols) && symbols.ValueKind == JsonValueKind.Object,
            "Project symbols must be an object.");
        foreach (JsonProperty symbol in symbols.EnumerateObject())
        {
            Require(symbol.Value.ValueKind == JsonValueKind.Object && !symbol.Value.EnumerateObject().Any(property =>
                string.Equals(property.Name, "fileRename", StringComparison.OrdinalIgnoreCase)),
                "Symbol-driven filename changes are not qualified for project configuration.");
        }
        Require(symbols.TryGetProperty("Framework", out JsonElement framework) && framework.ValueKind == JsonValueKind.Object
            && framework.TryGetProperty("type", out JsonElement frameworkType) && frameworkType.ValueKind == JsonValueKind.String
            && ReadString(frameworkType, "Framework type") == "parameter"
            && framework.TryGetProperty("datatype", out JsonElement dataType) && dataType.ValueKind == JsonValueKind.String
            && ReadString(dataType, "Framework datatype") == "choice"
            && framework.TryGetProperty("replaces", out JsonElement replacement) && replacement.ValueKind == JsonValueKind.String
            && ReadString(replacement, "Framework replacement") == "TargetFrameworkValue"
            && framework.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array
            && choices.EnumerateArray().Any(choice => choice.ValueKind == JsonValueKind.Object
                && choice.TryGetProperty("choice", out JsonElement value) && value.ValueKind == JsonValueKind.String
                && ReadString(value, "Framework choice") == "net10.0"),
            "The qualified .NET layout requires the observed native Framework parameter with net10.0 support.");
        Require(root.TryGetProperty("tags", out JsonElement tags) && tags.ValueKind == JsonValueKind.Object
            && tags.TryGetProperty("type", out JsonElement type) && ReadString(type, "type") == "project"
            && tags.TryGetProperty("language", out JsonElement language) && language.ValueKind == JsonValueKind.String,
            "The qualified .NET layout requires a native project type and language.");
        string declaredLanguage = ReadString(tags.GetProperty("language"), "language");
        Require(declaredLanguage is "C#" or "F#", "The qualified .NET layout supports only C# and F#.");
        Require(qualification.Anchor.PrimaryOutputIndex == 0
            && string.Equals(qualification.Anchor.Stack, "dotnet", StringComparison.OrdinalIgnoreCase)
            && qualification.Anchor.Language == declaredLanguage,
            "The qualified .NET project anchor must match its selected stack and language.");
        Require(root.TryGetProperty("sourceName", out JsonElement sourceName) && ReadString(sourceName, "sourceName") == "Company.FunctionApp",
            "Only the observed upstream .NET project sourceName is qualified.");
        string sourceOutput = "Company.FunctionApp" + (declaredLanguage == "F#" ? ".fsproj" : ".csproj");
        Require(root.TryGetProperty("primaryOutputs", out JsonElement outputs) && outputs.ValueKind == JsonValueKind.Array
            && outputs.GetArrayLength() == 1, "The qualified .NET layout requires exactly one native project primary output.");
        Require(HasOnlyProperty(outputs[0], "path", out JsonElement path) && ReadString(path, "primary output") == sourceOutput,
            "Only the observed unconditional .NET project primary-output path is qualified.");
        return [new(0, sourceOutput, "dotnet", declaredLanguage)];
    }

    internal static void ValidateCapture(TemplateConfigurationSnapshot configuration, ICreationEffects effects, string outputDirectory,
        NativeProjectLayoutQualification qualification, IReadOnlyDictionary<string, string?> parameters)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException("The qualified .NET layout requires an absolute invocation root.", nameof(outputDirectory));
        }
        TemplateProjectConfigurationDeclaration declaration = Read(configuration, qualification).Single();
        Require(parameters.TryGetValue("Framework", out string? framework) && framework == "net10.0",
            "Qualified .NET project creation requires an explicit Framework value of net10.0.");
        StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        outputDirectory = Path.GetFullPath(outputDirectory);
        string[] primary = [.. effects.CreationResult.PrimaryOutputs.Select(output => Path.GetFullPath(Path.Combine(outputDirectory, output.Path)))];
        string[] changed = [.. effects.FileChanges.Select(change => Path.GetFullPath(Path.Combine(outputDirectory, change.TargetRelativePath)))];
        string[] generated = [.. effects.FileChanges.Where(change => change.ChangeKind is ChangeKind.Create or ChangeKind.Change or ChangeKind.Overwrite)
            .Select(change => Path.GetFullPath(Path.Combine(outputDirectory, change.TargetRelativePath)))];
        Require(effects.CreationResult.PrimaryOutputs.All(output => !Path.IsPathRooted(output.Path)
                && !output.Path.Split('/', '\\').Any(segment => segment == ".."))
            && primary.Length == 1 && comparer.Equals(Path.GetDirectoryName(primary[0]), outputDirectory)
            && Path.GetExtension(primary[0]) == Path.GetExtension(declaration.Path) && generated.Contains(primary[0], comparer),
            "The qualified .NET primary output must resolve to one generated project file directly inside the invocation root.");
        string[] roots = [.. generated.Where(path => comparer.Equals(Path.GetFileName(path), "host.json"))
            .Select(path => Path.GetDirectoryName(path)!).Distinct(comparer)];
        Require(changed.All(path => IsWithin(outputDirectory, path)) && roots.Length == 1 && comparer.Equals(roots[0], outputDirectory),
            "The qualified .NET layout must remain inside one invocation root with one generated host.json.");
        Require(!changed.Any(path => string.Equals(Path.GetFileName(path), "config.json", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), ".func", StringComparison.OrdinalIgnoreCase)),
            "Template content must not write CLI-owned project configuration.");
        Require(generated.Count(path => Path.GetExtension(path) is ".csproj" or ".fsproj") == 1,
            "The qualified .NET layout must generate exactly one project file.");
        Require(HasNativeMapping(effects, declaration.Path, primary[0], outputDirectory)
            && HasNativeMapping(effects, "_gitignore", Path.Combine(outputDirectory, ".gitignore"), outputDirectory),
            "The native capture must map the selected project source and static ignore rename to their generated outputs.");
    }

    private static void ValidateTopology(JsonElement root)
    {
        Require(root.TryGetProperty("sources", out JsonElement sources) && sources.ValueKind == JsonValueKind.Array
            && sources.GetArrayLength() == 1 && HasOnlyProperty(sources[0], "modifiers", out JsonElement modifiers)
            && modifiers.ValueKind == JsonValueKind.Array && modifiers.GetArrayLength() == 1
            && HasOnlyProperty(modifiers[0], "rename", out JsonElement rename)
            && HasOnlyProperty(rename, "_gitignore", out JsonElement destination)
            && destination.ValueKind == JsonValueKind.String && ReadString(destination, "ignore rename") == ".gitignore",
            "Only the observed static _gitignore to .gitignore source rename is qualified for .NET projects.");
        foreach (string property in new[] { "specialCustomOperations", "globalCustomOperations", "baselines", "additionalConfigFiles" })
        {
            Require(!root.EnumerateObject().Any(candidate => string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase)),
                $"Project topology property '{property}' is not qualified for this creation path.");
        }
    }

    private static bool HasOnlyProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Count() == 1 && element.TryGetProperty(name, out value);
    }

    private static bool HasNativeMapping(ICreationEffects effects, string source, string target, string outputDirectory)
        => effects.FileChanges.OfType<IFileChange2>().Count(change =>
            (change.SourceRelativePath == source || change.SourceRelativePath == "./" + source)
            && Path.GetFullPath(Path.Combine(outputDirectory, change.TargetRelativePath)) == target
            && change.ChangeKind is ChangeKind.Create or ChangeKind.Change or ChangeKind.Overwrite) == 1;

    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ReadString(JsonElement value, string name)
    {
        Require(value.ValueKind == JsonValueKind.String, $"'{name}' must be a nonempty raw string.");
        try
        {
            string? text = value.GetString();
            Require(!string.IsNullOrWhiteSpace(text), $"'{name}' must be a nonempty raw string.");
            return text!;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidTemplateMetadataException($"Project configuration contains invalid Unicode text: {exception.Message}", exception);
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string name;
                try
                {
                    name = property.Name;
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidTemplateMetadataException(
                        $"Project configuration contains an invalid Unicode property name: {exception.Message}", exception);
                }
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidTemplateMetadataException(message);
        }
    }
}
