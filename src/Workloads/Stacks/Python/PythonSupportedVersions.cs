// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Python versions the Azure Functions Python worker supports per runtime identifier.
/// </summary>
internal static class PythonSupportedVersions
{
    internal const string PreferredVersion = "3.13";
    internal const string WinArm64Rid = "win-arm64";

    // Mirrors the payloads shipped by the Python worker workload (src/Workloads/Workers/Python).
    // Used only when the installed worker payload can't be read, e.g. during `func init`.
    private static readonly string[] _defaultFallback = ["3.10", "3.11", "3.12", "3.13", "3.14", "3.15"];
    private static readonly string[] _winArm64Fallback = ["3.15"];

    internal static IReadOnlyList<string> GetFallback(string runtimeIdentifier) =>
        string.Equals(runtimeIdentifier, WinArm64Rid, StringComparison.OrdinalIgnoreCase) ? _winArm64Fallback : _defaultFallback;

    /// <summary>
    /// Returns <see cref="PreferredVersion"/> when supported, otherwise the newest supported version.
    /// </summary>
    internal static string GetDefault(IReadOnlyList<string> supported)
    {
        ArgumentNullException.ThrowIfNull(supported);
        if (supported.Count == 0)
        {
            throw new ArgumentException("At least one supported version is required.", nameof(supported));
        }

        return supported.Contains(PreferredVersion, StringComparer.Ordinal)
            ? PreferredVersion
            : supported.MaxBy(Version.Parse)!;
    }

    /// <summary>
    /// Maps a runtime identifier to the <c>{os}</c> and <c>{architecture}</c> folder names used in the worker payload.
    /// </summary>
    internal static (string Os, string Architecture) GetWorkerPlatform(string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);

        int separator = runtimeIdentifier.LastIndexOf('-');
        string os = separator < 0 ? runtimeIdentifier : runtimeIdentifier[..separator];
        string arch = separator < 0 ? string.Empty : runtimeIdentifier[(separator + 1)..];

        string workerOs = os.ToLowerInvariant() switch
        {
            "win" => "WINDOWS",
            "osx" => "OSX",
            _ => "LINUX",
        };

        string workerArch = arch.ToLowerInvariant() switch
        {
            "arm64" => "Arm64",
            "x86" => "X86",
            _ => "X64",
        };

        return (workerOs, workerArch);
    }

    internal static string GetCurrentRuntimeIdentifier()
    {
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };
        return $"{os}-{arch}";
    }
}
