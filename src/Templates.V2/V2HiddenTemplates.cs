// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// Template ids the V2 provider suppresses from <c>func new</c> listings and
/// the interactive picker. Apply is intentionally not filtered so callers who
/// already know the id can still pass <c>--template &lt;id&gt;</c>.
/// </summary>
internal static class V2HiddenTemplates
{
    internal static readonly HashSet<string> Ids = new(StringComparer.OrdinalIgnoreCase)
    {
        // TypeScript
        "BlobTrigger-TypeScript",
        "EventGridBlobTrigger-TypeScript",
        "DurableFunctionsEntity-TypeScript",
        "DurableFunctionsOrchestrator-TypeScript",

        // JavaScript
        "DurableFunctionsEntity-JavaScript",
        "DurableFunctionsOrchestrator-JavaScript",

        // Python
        "DurableFunctionsEntityTrigger-Python",
        "DurableFunctionsOrchestration-Python",
    };

    public static bool IsHidden(string templateId) =>
        !string.IsNullOrWhiteSpace(templateId) && Ids.Contains(templateId);
}
