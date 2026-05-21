// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Resolves the full path to the <c>dotnet</c> muxer using well-known environment
/// variables and conventions, rather than relying on PATH resolution.
/// </summary>
internal sealed class DotnetPathResolver : IDotnetPathResolver
{
    private static readonly string _dotnetExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "dotnet.exe"
        : "dotnet";

    private string? _cachedPath;

    /// <summary>
    /// Returns the resolved path to the <c>dotnet</c> executable.
    /// </summary>
    public string Resolve()
    {
        if (_cachedPath is not null)
        {
            return _cachedPath;
        }

        _cachedPath = ResolveCore();
        return _cachedPath;
    }

    private static string ResolveCore()
    {
        // 1. DOTNET_HOST_PATH – set by the SDK to the absolute path of the running dotnet host.
        string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        // 2. Current process – if this tool is launched via `dotnet func`, the process is the muxer.
        string? processPath = Environment.ProcessPath;
        if (processPath is not null
            && Path.GetFileName(processPath).Equals(_dotnetExeName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(processPath))
        {
            return processPath;
        }

        // 3. DOTNET_ROOT (and architecture-specific variants) – standard variable pointing
        //    to the dotnet installation directory.
        foreach (string envVar in GetDotnetRootVariables())
        {
            string? dotnetRoot = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(dotnetRoot))
            {
                string candidate = Path.Combine(dotnetRoot, _dotnetExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // 4. Well-known default install locations (platform-specific).
        foreach (string defaultPath in GetDefaultInstallPaths())
        {
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }
        }

        throw new GracefulException(
            "Could not locate the dotnet host. Install the .NET SDK or set the DOTNET_HOST_PATH or DOTNET_ROOT environment variable.",
            isUserError: true);
    }

    private static IReadOnlyList<string> GetDotnetRootVariables()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X86 => ["DOTNET_ROOT(x86)", "DOTNET_ROOT"],
                Architecture.Arm64 => ["DOTNET_ROOT_ARM64", "DOTNET_ROOT"],
                _ => ["DOTNET_ROOT"]
            };
        }

        return ["DOTNET_ROOT"];
    }

    private static IReadOnlyList<string> GetDefaultInstallPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", _dotnetExeName)];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return
            [
                Path.Combine("/usr/local/share/dotnet", _dotnetExeName),
                Path.Combine("/opt/homebrew/bin", _dotnetExeName),
            ];
        }

        // Linux
        return [Path.Combine("/usr/share/dotnet", _dotnetExeName)];
    }
}
