// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Real-disk implementation of <see cref="IFuncTemplateFileSystem"/>.
/// </summary>
internal sealed class PhysicalFuncTemplateFileSystem : IFuncTemplateFileSystem
{
    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc />
    public void WriteAllText(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    /// <inheritdoc />
    public void AppendAllText(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(path, content);
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            : [];
}
