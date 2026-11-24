// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads;

internal static class WorkloadPackageTags
{
    public const string AliasPrefix = "alias:";
    public const string KindPrefix = "kind:";
    public const string RuntimeIdentifierPrefix = "rid:";
    public const string StackPrefix = "stack:";
    public const string WorkerRuntimePrefix = "worker-runtime:";

    public static IReadOnlyList<string> ParseValues(string? tags, string prefix)
        => [.. GetValues(tags, prefix).Select(value => value.ToLowerInvariant())];

    public static string? ParseLastValue(string? tags, string prefix)
        => ParseValues(tags, prefix).LastOrDefault();

    public static StackDescriptor? ParseStack(string? tags, string? title, string? description)
    {
        string? id = GetUniqueValue(tags, StackPrefix);
        string? runtime = GetUniqueValue(tags, WorkerRuntimePrefix);
        return id is not null && runtime is not null && !string.IsNullOrWhiteSpace(title)
            ? new StackDescriptor(id, runtime, title, description)
            : null;
    }

    private static string? GetUniqueValue(string? tags, string prefix)
        => GetValues(tags, prefix).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray() is [string value] ? value : null;

    private static IEnumerable<string> GetValues(string? tags, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (string.IsNullOrWhiteSpace(tags))
        {
            yield break;
        }

        foreach (string tag in tags.Split([' ', ',', ';', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = tag[prefix.Length..].Trim();
            if (value.Length > 0)
            {
                yield return value;
            }
        }
    }
}
