// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

internal static class WorkloadPackageTags
{
    public const string AliasPrefix = "alias:";
    public const string KindPrefix = "kind:";
    public const string RuntimeIdentifierPrefix = "rid:";

    public static IReadOnlyList<string> ParseValues(string? tags, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        List<string> values = [];
        foreach (string tag in tags.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = tag[prefix.Length..].Trim();
            if (value.Length > 0)
            {
                values.Add(value.ToLowerInvariant());
            }
        }

        return values;
    }

    public static string? ParseLastValue(string? tags, string prefix)
        => ParseValues(tags, prefix).LastOrDefault();
}
