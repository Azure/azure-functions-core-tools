// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Default <see cref="IGitRunner"/> backed by the system <c>git</c> executable.
/// Argument-array invocation prevents URL/path injection; environment variables
/// disable every interactive credential helper so a missing prompt can't hang us.
/// </summary>
internal sealed partial class GitRunner : IGitRunner
{
    // Sample-repo clones are typically a few hundred KB. 60s is generous for
    // a flaky proxy while failing fast on a deadlocked credential helper.
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(60);

    // 2.25 is the first release with cone-mode sparse-checkout via `--sparse`.
    private static readonly Version _minVersion = new(2, 25);

    [GeneratedRegex(@"git version (\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo psi = CreateNonInteractiveStartInfo();
            psi.ArgumentList.Add("--version");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return false;
            }

            Match m = VersionRegex().Match(stdout);
            if (!m.Success)
            {
                return false;
            }

            int major = int.Parse(m.Groups[1].Value);
            int minor = int.Parse(m.Groups[2].Value);
            return new Version(major, minor) >= _minVersion;
        }
        catch (Win32Exception)
        {
            // git not on PATH.
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public async Task<GitCloneResult> ShallowCloneAsync(
        string repositoryUrl,
        string? gitRef,
        string targetDirectory,
        string? folderPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        // Reject refs and folder paths that begin with '-' even though the '--'
        // end-of-options sentinel below blocks them. Belt-and-braces against an
        // attacker smuggling "--upload-pack=…" via a malicious manifest.
        if (gitRef is not null && gitRef.StartsWith('-'))
        {
            throw new InvalidOperationException(
                $"Invalid git ref '{gitRef}': refs must not start with '-'.");
        }

        bool useSparseCheckout = !string.IsNullOrWhiteSpace(folderPath) && folderPath != ".";
        string? normalizedFolderPath = useSparseCheckout
            ? folderPath!.Replace('\\', '/').TrimStart('/').TrimEnd('/')
            : null;

        if (normalizedFolderPath is not null &&
            (normalizedFolderPath.StartsWith('-') || normalizedFolderPath.Contains("..")))
        {
            throw new InvalidOperationException(
                $"Invalid folder path '{folderPath}': must not start with '-' or contain '..'.");
        }

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_defaultTimeout);

        GitCloneResult cloneResult = await RunCloneAsync(
            repositoryUrl, gitRef, targetDirectory, useSparseCheckout,
            cancellationToken, timeoutCts.Token);

        if (cloneResult.ExitCode != 0 || !useSparseCheckout)
        {
            return cloneResult;
        }

        // Second invocation materialises just the requested subfolder.
        return await RunSparseSetAsync(
            targetDirectory, normalizedFolderPath!,
            cancellationToken, timeoutCts.Token);
    }

    private static async Task<GitCloneResult> RunCloneAsync(
        string repositoryUrl,
        string? gitRef,
        string targetDirectory,
        bool sparse,
        CancellationToken userToken,
        CancellationToken timeoutToken)
    {
        ProcessStartInfo psi = CreateNonInteractiveStartInfo();
        BuildCloneArgs(psi.ArgumentList, repositoryUrl, gitRef, targetDirectory, sparse);
        return await RunAndAwaitAsync(psi, userToken, timeoutToken, "git clone");
    }

    /// <summary>
    /// Builds the argument list for <c>git clone</c>. Exposed as a static helper
    /// so tests can assert the constructed argv without spawning git.
    /// When <paramref name="gitRef"/> is null, empty, or <c>"HEAD"</c>, the
    /// <c>--branch</c> arg is omitted so git clones the remote's default branch.
    /// </summary>
    internal static void BuildCloneArgs(
        IList<string> args,
        string repositoryUrl,
        string? gitRef,
        string targetDirectory,
        bool sparse)
    {
        args.Add("clone");
        args.Add("--depth");
        args.Add("1");
        if (!string.IsNullOrWhiteSpace(gitRef) &&
            !string.Equals(gitRef, "HEAD", StringComparison.Ordinal))
        {
            args.Add("--branch");
            args.Add(gitRef);
            args.Add("--single-branch");
        }

        if (sparse)
        {
            // `--sparse` requires git 2.25+. Caller (IsAvailableAsync) gates this.
            args.Add("--sparse");
        }

        args.Add("--config");
        args.Add("core.autocrlf=false");
        args.Add("--"); // end-of-options sentinel
        args.Add(repositoryUrl);
        args.Add(targetDirectory);
    }

    private static async Task<GitCloneResult> RunSparseSetAsync(
        string targetDirectory,
        string folderPath,
        CancellationToken userToken,
        CancellationToken timeoutToken)
    {
        ProcessStartInfo psi = CreateNonInteractiveStartInfo();
        psi.WorkingDirectory = targetDirectory;
        BuildSparseSetArgs(psi.ArgumentList, folderPath);
        return await RunAndAwaitAsync(psi, userToken, timeoutToken, "git sparse-checkout set");
    }

    /// <summary>
    /// Builds the argument list for <c>git sparse-checkout set</c>. Exposed as
    /// a static helper so tests can assert the constructed argv without spawning git.
    /// </summary>
    internal static void BuildSparseSetArgs(IList<string> args, string folderPath)
    {
        args.Add("sparse-checkout");
        args.Add("set");
        args.Add("--"); // end-of-options sentinel
        args.Add(folderPath);
    }

    private static async Task<GitCloneResult> RunAndAwaitAsync(
        ProcessStartInfo psi,
        CancellationToken userToken,
        CancellationToken timeoutToken,
        string commandLabel)
    {
        using Process? process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{commandLabel}' process.");

        string stderr = string.Empty;
        try
        {
            stderr = await process.StandardError.ReadToEndAsync(timeoutToken);
            await process.WaitForExitAsync(timeoutToken);
        }
        catch (OperationCanceledException) when (!userToken.IsCancellationRequested)
        {
            // Timeout fired (user didn't cancel). It's possible the process
            // exited normally in the same tick — if so, return its real result
            // rather than misreporting a timeout.
            if (process.HasExited)
            {
                return new GitCloneResult(process.ExitCode, stderr);
            }

            // Process is still alive — kill the runaway tree so we don't leak it.
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
            throw new InvalidOperationException(
                $"{commandLabel} exceeded the {_defaultTimeout.TotalSeconds:0}-second timeout.");
        }

        return new GitCloneResult(process.ExitCode, stderr);
    }

    private static ProcessStartInfo CreateNonInteractiveStartInfo()
    {
        ProcessStartInfo psi = new()
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // Disable every interactive prompt path so a missing credential helper
        // can never deadlock our process waiting for stdin.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"] = "echo";
        psi.Environment["SSH_ASKPASS"] = "echo";
        psi.Environment["GCM_INTERACTIVE"] = "Never";

        return psi;
    }
}
