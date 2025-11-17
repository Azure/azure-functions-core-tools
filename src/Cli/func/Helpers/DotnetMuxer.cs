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
        string muxerPath;

        // Most scenarios are running dotnet.dll as the app
        // Root directory with muxer should be two above app base: <root>/sdk/<version>
        string rootPath = Path.GetDirectoryName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)));
        if (rootPath is not null)
        {
            muxerPath = Path.Combine(rootPath, $"{_muxerName}{_exeSuffix}");
            if (File.Exists(muxerPath))
            {
                return muxerPath;
            }
        }

        // Best-effort search for muxer.
        muxerPath = Environment.ProcessPath;

        // The current process should be dotnet in most normal scenarios except when dotnet.dll is loaded in a custom host like the testhost
        if (muxerPath is not null && !Path.GetFileNameWithoutExtension(muxerPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // SDK sets DOTNET_HOST_PATH as absolute path to current dotnet executable
            muxerPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (muxerPath is null)
            {
                // fallback to DOTNET_ROOT which typically holds some dotnet executable
                string root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                if (root is not null)
                {
                    muxerPath = Path.Combine(root, $"dotnet{_exeSuffix}");
                }
            }
        }

        if (muxerPath is null)
        {
            throw new InvalidOperationException("Unable to locate dotnet multiplexer");
        }

        return muxerPath;
    }
}
