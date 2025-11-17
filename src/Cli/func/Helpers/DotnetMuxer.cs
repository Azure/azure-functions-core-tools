// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/main/src/Cli/Microsoft.DotNet.Cli.Utils/Muxer.cs
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Helpers;

public class DotnetMuxer
{
    public static readonly string MuxerName = "dotnet";

    private readonly string _muxerPath;

    public static readonly string ExeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty;

    public DotnetMuxer()
    {
        // Most scenarios are running dotnet.dll as the app
        // Root directory with muxer should be two above app base: <root>/sdk/<version>
        string rootPath = Path.GetDirectoryName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)));
        if (rootPath is not null)
        {
            string muxerPathMaybe = Path.Combine(rootPath, $"{MuxerName}{FileNameSuffixes.CurrentPlatform.Exe}");
            if (File.Exists(muxerPathMaybe))
            {
                _muxerPath = muxerPathMaybe;
            }
        }

        if (_muxerPath is null)
        {
            // Best-effort search for muxer.
            string processPath = Environment.ProcessPath;

            // The current process should be dotnet in most normal scenarios except when dotnet.dll is loaded in a custom host like the testhost
            if (processPath is not null && !Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                // SDK sets DOTNET_HOST_PATH as absolute path to current dotnet executable
                processPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                if (processPath is null)
                {
                    // fallback to DOTNET_ROOT which typically holds some dotnet executable
                    var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                    if (root is not null)
                    {
                        processPath = Path.Combine(root, $"dotnet{ExeSuffix}");
                    }
                }
            }

            _muxerPath = processPath;
        }
    }

    internal string SharedFxVersion
    {
        get
        {
            var depsFile = new FileInfo(GetDataFromAppDomain("FX_DEPS_FILE") ?? string.Empty);
            return depsFile.Directory?.Name ?? string.Empty;
        }
    }

    public string MuxerPath
    {
        get
        {
            if (_muxerPath == null)
            {
                throw new InvalidOperationException("Unable to locate dotnet multiplexer");
            }

            return _muxerPath;
        }
    }

    public static string GetDataFromAppDomain(string propertyName)
    {
        return AppContext.GetData(propertyName) as string;
    }
}
