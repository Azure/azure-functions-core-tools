// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Production <see cref="IFuncInvocation"/>. Detection runs once on first
/// access and is best-effort; any failure resolves to the safe defaults
/// (<c>RecommendedName == "func"</c>, no conflict).
/// </summary>
internal sealed class FuncInvocation : IFuncInvocation
{
    public const string DefaultName = "func";
    public const string AliasName = "func5";

    private readonly Lazy<Detection> _detection;

    public FuncInvocation(IFuncOnPathResolver pathResolver)
        : this(pathResolver, Environment.ProcessPath)
    {
    }

    // Test seam: lets unit tests pin the current process path so detection
    // is deterministic without touching Environment.ProcessPath.
    internal FuncInvocation(IFuncOnPathResolver pathResolver, string? processPath)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        _detection = new Lazy<Detection>(() => Detect(pathResolver, processPath));
    }

    public string RecommendedName => _detection.Value.RecommendedName;

    public bool ConflictDetected => _detection.Value.ConflictingPath is not null;

    public string? ConflictingPath => _detection.Value.ConflictingPath;

    private static Detection Detect(IFuncOnPathResolver pathResolver, string? processPath)
    {
        try
        {
            string? resolved = pathResolver.ResolveFuncOnPath();
            if (resolved is null || string.IsNullOrEmpty(processPath))
            {
                return Detection.NoConflict;
            }

            string normalizedResolved = TryCanonicalize(resolved);
            string normalizedProcess = TryCanonicalize(processPath);

            StringComparison comparison = OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (string.Equals(normalizedResolved, normalizedProcess, comparison))
            {
                return Detection.NoConflict;
            }

            return new Detection(AliasName, normalizedResolved);
        }
        catch
        {
            return Detection.NoConflict;
        }
    }

    private static string TryCanonicalize(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }

        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
            return target?.FullName ?? fullPath;
        }
        catch
        {
            return fullPath;
        }
    }

    private readonly record struct Detection(string RecommendedName, string? ConflictingPath)
    {
        public static Detection NoConflict { get; } = new(DefaultName, null);
    }
}
