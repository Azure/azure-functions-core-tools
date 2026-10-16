// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Limits the staged consumer to absent or empty targets, without creating or clearing directories.
/// </summary>
internal class InitExecutionTargetInspector
{
    public virtual string? GetDiagnostic(WorkingDirectory target)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (DirectoryInfo? directory = target.Info; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(directory.FullName))
            {
                return $"'{directory.FullName}' is a file. Supply an empty project directory.";
            }
        }

        return Directory.Exists(target.Info.FullName) && Directory.EnumerateFileSystemEntries(target.Info.FullName).Any()
            ? "The staged INIT consumer requires an empty target. Use the existing public 'func init' for adoption or repair; this harness never clears content."
            : null;
    }
}
