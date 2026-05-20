// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Executes <c>dotnet</c> CLI commands in a child process.
/// </summary>
internal sealed class DotnetCliRunner : IDotnetCliRunner
{
    private static readonly string _dotnetExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "dotnet.exe"
        : "dotnet";

    private static readonly Lazy<string> _dotnetPath = new(ResolveDotnetPath);

    public async Task RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo psi = new()
        {
            FileName = _dotnetPath.Value,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        try
        {
            // Read both streams concurrently to avoid deadlock when either
            // buffer fills while the other is being awaited.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cancellationToken);

            string stdout = stdoutTask.Result;
            string stderr = stderrTask.Result;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"'dotnet {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}{stdout}");
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Resolves the full path to the <c>dotnet</c> muxer using well-known environment
    /// variables and conventions, rather than relying on PATH resolution.
    /// </summary>
    private static string ResolveDotnetPath()
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

        // 5. Fallback – rely on PATH (best effort).
        return _dotnetExeName;
    }

    private static IReadOnlyList<string> GetDotnetRootVariables()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X86 => ["DOTNET_ROOT(x86)", "DOTNET_ROOT"],
                Architecture.Arm64 => ["DOTNET_ROOT", "DOTNET_ROOT(ARM64)"],
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
