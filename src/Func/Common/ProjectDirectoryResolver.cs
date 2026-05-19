// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

internal static class ProjectDirectoryResolver
{
    public static bool IsProjectDirectory(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string candidate = Normalize(directory.FullName);
        string projectDirectory = Normalize(Environment.CurrentDirectory);
        return string.Equals(candidate, projectDirectory, GetPathComparison());
    }

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
