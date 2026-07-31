// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// In-memory <see cref="IFuncTemplateFileSystem"/> for post-action handler
/// unit tests. Records writes so tests can assert nothing was written to the
/// project on a failed append.
/// </summary>
internal sealed class InMemoryFuncTemplateFileSystem : IFuncTemplateFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _writes = [];

    public IReadOnlyList<string> WrittenPaths => _writes;

    public bool ThrowOnWrite { get; set; }

    public void Seed(string path, string content) => _files[Normalize(path)] = content;

    public string? Peek(string path) => _files.TryGetValue(Normalize(path), out string? content) ? content : null;

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public string ReadAllText(string path) => _files.TryGetValue(Normalize(path), out string? content)
        ? content
        : throw new FileNotFoundException("Not found in fake filesystem.", path);

    public void WriteAllText(string path, string content)
    {
        if (ThrowOnWrite)
        {
            throw new IOException("Simulated write failure.");
        }

        _files[Normalize(path)] = content;
        _writes.Add(Normalize(path));
    }

    public void AppendAllText(string path, string content)
    {
        if (ThrowOnWrite)
        {
            throw new IOException("Simulated write failure.");
        }

        string key = Normalize(path);
        _files[key] = (_files.TryGetValue(key, out string? existing) ? existing : string.Empty) + content;
        _writes.Add(key);
    }

    public void DeleteFile(string path) => _files.Remove(Normalize(path));

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern)
    {
        string dir = Normalize(directory);
        var matcher = new Regex(
            "^" + Regex.Escape(searchPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            RegexOptions.IgnoreCase);

        return _files.Keys
            .Where(p => string.Equals(Path.GetDirectoryName(p), dir, StringComparison.OrdinalIgnoreCase)
                && matcher.IsMatch(Path.GetFileName(p)))
            .ToList();
    }

    private static string Normalize(string path) => Path.GetFullPath(path);
}
