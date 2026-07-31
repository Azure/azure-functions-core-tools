// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// The func-owned append post-action declaration read from a template's
/// <c>template.json</c>: the symbol names that hold the target file and the
/// app/blueprint object, resolved to their values at scaffold time.
/// </summary>
/// <param name="TargetFileParam">Symbol name whose value is the target file, or <c>null</c>.</param>
/// <param name="AppObjectParam">Symbol name whose value is the app/blueprint object, or <c>null</c>.</param>
/// <param name="DeleteStagedFile">Whether the staged snippet should be removed after a successful append.</param>
internal sealed record FuncAppendActionConfig(string? TargetFileParam, string? AppObjectParam, bool DeleteStagedFile);

/// <summary>
/// Extracts func-specific declarations from a raw <c>template.json</c> that the
/// engine's <see cref="Microsoft.TemplateEngine.Abstractions.ITemplateInfo"/>
/// projection does not surface (the append post-action's argument mapping).
/// </summary>
internal static class FuncTemplateConfigParser
{
    /// <summary>
    /// Returns the append post-action's argument mapping, or <c>null</c> when
    /// the JSON is absent, malformed, or declares no append action.
    /// </summary>
    internal static FuncAppendActionConfig? TryReadAppendAction(string? templateJson)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(templateJson);
            if (!document.RootElement.TryGetProperty("postActions", out JsonElement postActions) ||
                postActions.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement action in postActions.EnumerateArray())
            {
                if (!TryGetGuid(action, "actionId", out Guid actionId) || actionId != FuncPostActionIds.Append)
                {
                    continue;
                }

                if (!action.TryGetProperty("args", out JsonElement args) || args.ValueKind != JsonValueKind.Object)
                {
                    return new FuncAppendActionConfig(null, null, false);
                }

                return new FuncAppendActionConfig(
                    GetString(args, "targetFileParam"),
                    GetString(args, "appObjectParam"),
                    string.Equals(GetString(args, "deleteStagedFile"), "true", StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            Guid.TryParse(property.GetString(), out value);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
