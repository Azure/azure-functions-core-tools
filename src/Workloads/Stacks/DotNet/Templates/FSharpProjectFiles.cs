// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;

#if FUNC_HOST_DOTNET_TEMPLATES
namespace Azure.Functions.Cli.Templates.DotNet.Shared;
#else
namespace Azure.Functions.Cli.Workloads.DotNet;
#endif

/// <summary>
/// Guards and atomically replaces a selected F# project without rewriting unrelated project bytes.
/// </summary>
internal class FSharpProjectFiles
{
    public virtual Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken) => File.ReadAllBytesAsync(path, cancellationToken);

    public virtual void ValidatePath(string path, string projectDirectory, bool mustExist)
    {
        string relative = Path.GetRelativePath(projectDirectory, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("F# compile repair paths must remain inside the selected project.");
        }

        string? current = path;
        while (current is not null && !string.Equals(Path.TrimEndingDirectorySeparator(current),
            Path.TrimEndingDirectorySeparator(projectDirectory), StringComparison.Ordinal))
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (current == path && (attributes & FileAttributes.Directory) != 0))
                {
                    throw new InvalidOperationException("F# compile repair refuses linked or occupied file paths.");
                }
            }
            catch (FileNotFoundException) when (!mustExist)
            {
                // Preview permits only the exact not-yet-created native source file.
            }
            catch (DirectoryNotFoundException) when (!mustExist)
            {
                // Preview permits a missing generated source, never a missing selected project.
            }

            current = Path.GetDirectoryName(current);
        }

        if (mustExist && !File.Exists(path))
        {
            throw new FileNotFoundException("The selected F# compile repair input is missing.", path);
        }
    }

    public virtual async Task ReplaceAsync(string projectPath, string expectedSha256, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(projectPath)!;
        ValidatePath(projectPath, directory, mustExist: true);
        string temporary = Path.Combine(directory, $".func-fsharp-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePath(projectPath, directory, mustExist: true);
            byte[] current = await ReadAsync(projectPath, cancellationToken);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(current)), expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected F# project changed before the compile update was committed.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, projectPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // Best-effort temporary-file cleanup must not hide the update's original failure.
            }
        }
    }
}
