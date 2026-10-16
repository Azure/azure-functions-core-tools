// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Templates.V2;

namespace Azure.Functions.Cli.Templates.Node;

/// <summary>
/// Validates the association between an original Node job body and its selected native source without selecting output paths.
/// </summary>
internal static class NodeItemSourceQualifier
{
    internal static void Validate(NodeItemSource source, string? language)
    {
        ArgumentNullException.ThrowIfNull(source);
        string extension = language switch
        {
            "JavaScript" => ".js",
            "TypeScript" => ".ts",
            _ => throw Invalid("Node source qualification requires the selected canonical JavaScript or TypeScript language."),
        };
        NewTemplate template = source.Template;
        if (template.Jobs is not { Count: 1 } || template.Jobs[0] is not { Type: "CreateNewApp", Actions: { Count: 2 } names }
            || template.Actions is not { Count: 2 } actions || template.Files is not { Count: 1 } files
            || names.Distinct(StringComparer.Ordinal).Count() != 2
            || actions.Any(action => string.IsNullOrWhiteSpace(action.Name))
            || actions.Select(action => action.Name).Distinct(StringComparer.Ordinal).Count() != 2)
        {
            throw Invalid("Node source qualification requires one original CreateNewApp job and an unambiguous single-file action graph.");
        }
        V2Action? read = actions.SingleOrDefault(action => action.Name == names[0]);
        V2Action? write = actions.SingleOrDefault(action => action.Name == names[1]);
        if (read is not { Type: "GetTemplateFileContent", FilePath: not null, AssignTo: not null }
            || write is not { Type: "WriteToFile", FilePath: not null, Source: not null }
            || read.AssignTo != write.Source || string.IsNullOrWhiteSpace(read.AssignTo)
            || read.ContinueOnError || write.ContinueOnError || write.FileContent is not null
            || !IsSegment(read.FilePath) || !files.TryGetValue(read.FilePath, out string? body) || body is null)
        {
            throw Invalid("The original Node write action is not associated with exactly one owned source body.");
        }
        string[] parts = write.FilePath.Split('/');
        if (parts.Length < 2 || parts[..^1].Any(part => !IsSegment(part))
            || parts[^1] != "$(FUNCTION_NAME_INPUT)" + extension)
        {
            throw Invalid("The original Node write path must contain a constant relative directory and one language-matched function-name filename.");
        }
        using var configuration = JsonDocument.Parse(source.Configuration);
        JsonElement root = configuration.RootElement;
        string sourceName = NodeItemCompatibility.Text(root, "sourceName");
        JsonElement declaredSymbols = NodeItemCompatibility.Property(root, "symbols");
        JsonElement intent = NodeItemCompatibility.Property(declaredSymbols, "InvocationOutputIntent");
        if (NodeItemCompatibility.Text(intent, "type") != "bind"
            || NodeItemCompatibility.Text(intent, "binding") != "host:func:item-output-intent")
        {
            throw Invalid("The authored Node invocation intent must remain a host binding, not a user parameter.");
        }
        const string suffix = "/.template.config/template.json";
        if (!IsSegment(sourceName) || !source.ConfigurationPath.EndsWith(suffix, StringComparison.Ordinal)
            || source.ContentPath != source.ConfigurationPath[..^suffix.Length] + "/" + sourceName + extension
            || root.TryGetProperty("additionalConfigFiles", out _)
            || (root.TryGetProperty("preferNameDirectory", out JsonElement preferDirectory) && preferDirectory.ValueKind != JsonValueKind.False)
            || (root.TryGetProperty("symbols", out JsonElement renames) && renames.ValueKind == JsonValueKind.Object
                && renames.EnumerateObject().Any(symbol => symbol.Value.ValueKind == JsonValueKind.Object
                    && symbol.Value.TryGetProperty("fileRename", out _))))
        {
            throw Invalid("The selected native Node source does not have the qualified single-file output layout.");
        }
        string expected = body.Replace("$(FUNCTION_NAME_INPUT)", sourceName, StringComparison.Ordinal);
        if (root.TryGetProperty("symbols", out JsonElement symbols) && symbols.TryGetProperty("AuthLevel", out JsonElement auth))
        {
            if (NodeItemCompatibility.Text(auth, "type") != "parameter" || NodeItemCompatibility.Text(auth, "datatype") != "choice"
                || NodeItemCompatibility.Text(auth, "defaultValue") != "anonymous"
                || NodeItemCompatibility.Text(auth, "replaces") != "__AUTH_LEVEL__")
            {
                throw Invalid("The selected native Node HTTP authorization substitution differs from its owned conversion.");
            }
            const string originalAuth = "authLevel: 'anonymous'";
            if (!expected.Contains(originalAuth, StringComparison.Ordinal))
            {
                throw Invalid("The original Node source is missing the declared HTTP authorization value.");
            }
            expected = expected.Replace(originalAuth, "authLevel: '__AUTH_LEVEL__'", StringComparison.Ordinal);
        }
        if (expected.ReplaceLineEndings("\n") != Encoding.UTF8.GetString(source.Content.Span).ReplaceLineEndings("\n"))
        {
            throw Invalid("The selected native Node payload differs from the original write action's owned source body.");
        }
    }

    private static bool IsSegment(string value) => !string.IsNullOrWhiteSpace(value) && value is not ("." or "..")
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private static InvalidDataException Invalid(string message) => new(message);
}
