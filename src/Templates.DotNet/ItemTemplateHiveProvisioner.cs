// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Text;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Default <see cref="IItemTemplateHiveProvisioner"/>. Resolves the dotnet
/// CLI from <c>PATH</c> (the templates workload's payload requires it for
/// scaffold-time shellout anyway).
/// </summary>
internal sealed class ItemTemplateHiveProvisioner(IItemTemplateHivePathProvider pathProvider)
    : IItemTemplateHiveProvisioner
{
    private static readonly TimeSpan _installTimeout = TimeSpan.FromMinutes(5);

    private readonly IItemTemplateHivePathProvider _pathProvider =
        pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));

    public async Task<string> EnsureProvisionedAsync(string installDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        DotNetSource? source = DotNetSourceReader.Load(installDirectory);
        if (source is null
            || string.IsNullOrWhiteSpace(source.PackageId)
            || string.IsNullOrWhiteSpace(source.Version))
        {
            throw new InvalidOperationException(
                $"DotNet templates workload at '{installDirectory}' is missing or has an invalid source.json " +
                $"(expected kind/packageId/version). Re-install the workload with: func workload install Azure.Functions.Cli.Workloads.Templates.DotNet.");
        }

        string hivePath = _pathProvider.HivePath;
        string sentinel = GetSentinelPath(hivePath, source.PackageId, source.Version);

        if (File.Exists(sentinel) && Directory.Exists(hivePath))
        {
            return hivePath;
        }

        Directory.CreateDirectory(hivePath);

        var args = new List<string>
        {
            "new",
            "install",
            $"{source.PackageId}::{source.Version}",
            "--debug:custom-hive",
            hivePath,
        };

        await RunDotnetAsync(args, cancellationToken);

        try
        {
            File.WriteAllText(sentinel, string.Empty);
        }
        catch (IOException)
        {
            // Non-fatal — next func new will reinstall; idempotent.
        }

        return hivePath;
    }

    private static string GetSentinelPath(string hivePath, string packageId, string version)
    {
        // One sentinel per (packageId, version) so future side-by-side
        // installs don't clobber each other.
        string fileName = $".installed.{packageId}.{version}".ToLowerInvariant();
        return Path.Combine(hivePath, fileName);
    }

    private static async Task RunDotnetAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = new(_installTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to launch 'dotnet new install' for item-template hive provisioning.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new InvalidOperationException(
                $"Provisioning the dotnet item-template hive timed out after {_installTimeout.TotalMinutes:0} minutes. Check your network and try again.");
        }

        if (process.ExitCode != 0)
        {
            string detail = stderr.ToString().Trim();
            string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : ": " + detail;
            throw new InvalidOperationException(
                $"'dotnet new install' exited with code {process.ExitCode}{suffix}. The dotnet item-template hive could not be provisioned.");
        }
    }
}
