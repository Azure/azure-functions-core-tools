// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Production <see cref="IFuncOnPathResolver"/> that walks the process
/// <c>PATH</c>. Per-entry I/O errors are swallowed so a single bad
/// directory does not suppress detection; detection is best-effort and
/// callers must treat <c>null</c> as "no opinion".
/// </summary>
internal sealed class PathFuncOnPathResolver(IProcessEnvironment environment) : IFuncOnPathResolver
{
    private const string FuncExecutableBaseName = "func";

    private readonly IProcessEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public string? ResolveFuncOnPath()
    {
        string? path = _environment.Get("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        IReadOnlyList<string> candidates = GetExecutableCandidates();

        foreach (string rawDir in path.Split(Path.PathSeparator))
        {
            string dir = rawDir.Trim();
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            foreach (string candidate in candidates)
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(Path.Combine(dir, candidate));
                }
                catch
                {
                    // Malformed PATH entry. Skip without failing the whole scan.
                    break;
                }

                try
                {
                    if (!File.Exists(fullPath))
                    {
                        continue;
                    }

                    if (!OperatingSystem.IsWindows() && !IsExecutable(fullPath))
                    {
                        continue;
                    }

                    return fullPath;
                }
                catch
                {
                    // File.Exists / GetUnixFileMode can throw on broken
                    // mounts or permission edges. Treat as "not here".
                }
            }
        }

        return null;
    }

    private IReadOnlyList<string> GetExecutableCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [FuncExecutableBaseName];
        }

        // Honour PATHEXT order so the same name resolution shells use is
        // applied. npm-installed v4 typically lands as func.cmd, so missing
        // .CMD here would defeat the whole detection.
        string pathExt = _environment.Get("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        var list = new List<string>();
        foreach (string ext in pathExt.Split(';'))
        {
            string trimmed = ext.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            list.Add(FuncExecutableBaseName + trimmed);
        }

        // Also probe the extensionless name as a last resort (some shells
        // accept it; cheap to include).
        list.Add(FuncExecutableBaseName);
        return list;
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static bool IsExecutable(string fullPath)
    {
        try
        {
            UnixFileMode mode = File.GetUnixFileMode(fullPath);
            const UnixFileMode anyExec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & anyExec) != 0;
        }
        catch
        {
            // GetUnixFileMode can fail on some filesystems; assume executable.
            return true;
        }
    }
}
