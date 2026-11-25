// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Extension methods for <see cref="PostActionContext" />.
/// </summary>
public static class PostActionContextExtensions
{
    extension(PostActionContext context)
    {
        /// <summary>
        /// Resolves configured source-file patterns to absolute generated target paths.
        /// </summary>
        /// <param name="argumentName">The argument containing source patterns.</param>
        /// <param name="matchCriteria">An optional predicate applied to absolute target paths.</param>
        /// <returns>Matching paths, or an empty list when the argument or source paths are unavailable.</returns>
        /// <exception cref="ArgumentNullException">The context or <paramref name="argumentName"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="argumentName"/> is empty or whitespace.</exception>
        public IReadOnlyList<string> GetConfiguredFiles(string argumentName, Func<string, bool>? matchCriteria = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(argumentName);
            if (context.FileMatcher is not { } matcher
                || !context.Arguments.TryGetValue(argumentName, out string? files)
                || string.IsNullOrWhiteSpace(files))
            {
                return [];
            }

            List<string> results = [];
            foreach (string pattern in ParsePaths(files))
            {
                string outputDirectory = Path.GetFullPath(context.OutputBasePath);
                string[] targetPaths = [.. matcher.MatchTargetPaths(pattern).Select(path => Path.GetFullPath(path, outputDirectory))];
                foreach (string path in targetPaths)
                {
                    if (matchCriteria is null || matchCriteria(path))
                    {
                        results.Add(path);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Resolves the named argument's paths against the output directory without matching them to generated files.
        /// </summary>
        /// <param name="argumentName">The argument containing paths.</param>
        /// <returns>Absolute paths in argument order, or an empty list when the argument is absent or blank.</returns>
        /// <exception cref="ArgumentNullException">The context or <paramref name="argumentName"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="argumentName"/> is empty or whitespace.</exception>
        public IReadOnlyList<string> GetConfiguredPaths(string argumentName)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(argumentName);
            if (!context.Arguments.TryGetValue(argumentName, out string? files) || string.IsNullOrWhiteSpace(files))
            {
                return [];
            }

            string outputDirectory = Path.GetFullPath(context.OutputBasePath);
            return [.. ParsePaths(files).Select(path => Path.GetFullPath(path, outputDirectory))];
        }
    }

    private static string[] ParsePaths(string files)
    {
        JsonNode? value;
        try
        {
            value = JsonNode.Parse(files, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException)
        {
            // Non-JSON values are semicolon-separated paths or patterns.
            return files.Split(';', StringSplitOptions.RemoveEmptyEntries);
        }

        if (value is JsonValue && value.GetValueKind() == JsonValueKind.String)
        {
            return value.GetValue<string>().Split(';', StringSplitOptions.RemoveEmptyEntries);
        }

        if (value is JsonArray array)
        {
            string[] paths = [.. array.Where(item => item?.GetValueKind() == JsonValueKind.String)
                .Select(item => item!.GetValue<string>())];
            if (paths.Length > 0)
            {
                return paths;
            }
        }

        return files.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }
}
