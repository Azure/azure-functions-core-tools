// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Default <see cref="IFuncProjectConfigReader"/>. Tolerant of missing,
/// malformed, or wrong-shape files so the resolver can fall through.
/// Mirrors <see cref="LocalSettingsReader"/>'s shape.
/// </summary>
internal sealed class FuncProjectConfigReader : IFuncProjectConfigReader
{
    internal const string ConfigFolderName = ".func";
    internal const string ConfigFileName = "config.json";

    private const string StackProperty = "stack";
    private const string LanguageProperty = "language";

    public FuncProjectConfig? Read(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string path = Path.Combine(directory.FullName, ConfigFolderName, ConfigFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? stack = ReadStringProperty(document.RootElement, StackProperty);
            string? language = ReadStringProperty(document.RootElement, LanguageProperty);

            if (stack is null && language is null)
            {
                return null;
            }

            return new FuncProjectConfig(stack, language);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadStringProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? raw = value.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }
}
