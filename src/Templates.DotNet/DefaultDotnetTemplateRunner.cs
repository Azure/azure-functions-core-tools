// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Text;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Production <see cref="IDotnetTemplateRunner"/> backed by
/// <see cref="Process"/>. Resolves <c>dotnet</c> from <c>PATH</c>; relies on
/// the user's environment having the SDK available (the templates workload
/// already required <c>dotnet</c> at install time to provision the hive).
/// </summary>
internal sealed class DefaultDotnetTemplateRunner : IDotnetTemplateRunner
{
    public async Task<DotnetTemplateRunResult> RunAsync(
        string shortName,
        DirectoryInfo workingDirectory,
        IReadOnlyList<string> extraArgs,
        CancellationToken cancellationToken,
        string? customHivePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortName);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(extraArgs);

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("new");
        psi.ArgumentList.Add(shortName);
        foreach (string arg in extraArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(customHivePath))
        {
            psi.ArgumentList.Add("--debug:custom-hive");
            psi.ArgumentList.Add(customHivePath);
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to launch 'dotnet new'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore: the process may have already exited, and a failed kill must not
                // mask the cancellation being rethrown below.
            }

            throw;
        }

        return new DotnetTemplateRunResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
