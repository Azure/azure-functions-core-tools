// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// The package manager that drives dependency installation for a Python project.
/// </summary>
internal enum PythonPackageManager
{
    Pip,
    Uv,
}

/// <summary>
/// Classifies a Python project as uv- or pip-managed from files at the project root.
/// </summary>
internal static partial class PythonPackageManagerDetector
{
    internal const string UvLockFileName = "uv.lock";
    internal const string PoetryLockFileName = "poetry.lock";
    internal const string PyProjectFileName = "pyproject.toml";
    internal const string RequirementsFileName = "requirements.txt";

    /// <summary>
    /// Rules, in order: <c>managed = false</c> in <c>[tool.uv]</c> → pip; <c>uv.lock</c> → uv;
    /// <c>poetry.lock</c> → pip; a <c>[tool.uv]</c> or <c>[tool.uv.*]</c> table other than
    /// <c>[tool.uv.pip]</c> → uv; otherwise pip.
    /// </summary>
    public static PythonPackageManager Detect(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);

        string pyProjectPath = Path.Combine(projectRoot, PyProjectFileName);
        (bool hasUvProjectTable, bool isUnmanaged) = File.Exists(pyProjectPath)
            ? ReadToolUvSettings(pyProjectPath)
            : (false, false);

        // `managed = false` opts out of uv's project locking and syncing; `uv sync` rejects such projects.
        if (isUnmanaged)
        {
            return PythonPackageManager.Pip;
        }

        if (File.Exists(Path.Combine(projectRoot, UvLockFileName)))
        {
            return PythonPackageManager.Uv;
        }

        if (File.Exists(Path.Combine(projectRoot, PoetryLockFileName)))
        {
            return PythonPackageManager.Pip;
        }

        return hasUvProjectTable ? PythonPackageManager.Uv : PythonPackageManager.Pip;
    }

    private static (bool HasUvProjectTable, bool IsUnmanaged) ReadToolUvSettings(string pyProjectPath)
    {
        bool hasUvProjectTable = false;
        bool isUnmanaged = false;
        bool inToolUvTable = false;

        try
        {
            foreach (string line in File.ReadLines(pyProjectPath))
            {
                Match header = TableHeaderRegex().Match(line);
                if (header.Success)
                {
                    string name = header.Groups["name"].Value.Trim();
                    bool isArrayTable = header.Groups["open"].Value.Length == 2;
                    inToolUvTable = !isArrayTable && name == "tool.uv";
                    hasUvProjectTable |= IsUvProjectTable(name);
                    continue;
                }

                if (inToolUvTable && ManagedFalseRegex().IsMatch(line))
                {
                    isUnmanaged = true;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return (hasUvProjectTable, isUnmanaged);
    }

    // [tool.uv.pip] only configures the `uv pip` interface and does not affect `uv sync`, `uv lock`, or `uv run`.
    private static bool IsUvProjectTable(string name)
        => name == "tool.uv"
            || (name.StartsWith("tool.uv.", StringComparison.Ordinal)
                && name != "tool.uv.pip"
                && !name.StartsWith("tool.uv.pip.", StringComparison.Ordinal));

    // Anchored at line start (after whitespace), so commented-out headers such as `# [tool.uv]` never match.
    // Matches both `[table]` and `[[array.table]]` headers (for example `[[tool.uv.index]]`).
    [GeneratedRegex(@"^\s*(?<open>\[\[?)(?<name>[^\[\]]+)\]\]?\s*(#.*)?$")]
    private static partial Regex TableHeaderRegex();

    [GeneratedRegex(@"^\s*managed\s*=\s*false\s*(#.*)?$")]
    private static partial Regex ManagedFalseRegex();
}
