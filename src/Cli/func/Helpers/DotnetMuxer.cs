// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Adapted from: https://github.com/dotnet/sdk/blob/main/src/Cli/Microsoft.DotNet.Cli.Utils/Muxer.cs
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Helpers;

internal static class DotnetMuxer
{
    private static readonly string _muxerName = "dotnet";
    private static readonly string _exeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty;

    /// <summary>
    /// Locates the dotnet muxer (dotnet executable).
    /// </summary>
    /// <returns>The full path to the dotnet executable.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the dotnet executable cannot be located.</exception>
    public static string GetMuxerPath()
    {
        // Most scenarios are running dotnet.dll as the app
        // Root directory with muxer should be two above app base: <root>/sdk/<version>
        string rootPath = Path.GetDirectoryName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)));
        if (rootPath is not null)
        {
            string muxerPath = Path.Combine(rootPath, $"{_muxerName}{_exeSuffix}");
            if (File.Exists(muxerPath))
            {
                return muxerPath;
            }
        }

        // Check if the current process is dotnet
        // In most scenarios, the process is dotnet, except when dotnet.dll is loaded
        // in a custom host like testhost, in which case we fall through to other checks
        string processPath = Environment.ProcessPath;
        if (processPath is not null && Path.GetFileNameWithoutExtension(processPath).Equals(_muxerName, StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        // SDK sets DOTNET_HOST_PATH as absolute path to current dotnet executable
        string envPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (envPath is not null)
        {
            return envPath;
        }

        // Fallback to DOTNET_ROOT which typically holds some dotnet executable
        string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (dotnetRoot is not null)
        {
            string muxerPath = Path.Combine(dotnetRoot, $"{_muxerName}{_exeSuffix}");
            if (File.Exists(muxerPath))
            {
                return muxerPath;
            }
        }

        // Search PATH environment variable
        string pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable is not null)
        {
            foreach (string directory in pathVariable.Split(Path.PathSeparator))
            {
                string potentialPath = Path.Combine(directory, $"{_muxerName}{_exeSuffix}");
                if (File.Exists(potentialPath))
                {
                    return potentialPath;
                }
            }
        }

        throw new InvalidOperationException($"Unable to locate {_muxerName} multiplexer");
    }
}
