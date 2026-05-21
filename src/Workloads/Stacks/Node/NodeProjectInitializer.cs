// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Scaffolds a Node.js (v4 model) Functions project, branching on <c>--language</c>
/// for JS or TS, merges the default extension bundle into <c>host.json</c>,
/// and optionally runs <c>npm install</c>.
/// </summary>
internal sealed class NodeProjectInitializer : IProjectInitializer
{
    private const string ProjectNamePlaceholder = "__PROJECT_NAME__";
    private static readonly Assembly _assembly = typeof(NodeProjectInitializer).Assembly;

    // Internal seam so tests can stub out the `npm install` invocation
    // without spawning real processes.
    internal Func<string, CancellationToken, Task<(int ExitCode, string Stderr)>> RunNpmInstall { get; set; } = DefaultRunNpmInstall;

    public string Stack => "node";

    public IReadOnlyList<string> SupportedLanguages { get; } = ["JavaScript", "TypeScript"];

    public Option<bool> NoBundleOption { get; private set; } = default!;

    public Option<BundleChannel> BundlesChannelOption { get; private set; } = default!;

    public Option<bool> SkipNpmInstallOption { get; private set; } = default!;

    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        NoBundleOption = registry.GetOrAdd(CommonInitOptions.NoBundle());

        BundlesChannelOption = registry.GetOrAdd(CommonInitOptions.BundlesChannel());

        SkipNpmInstallOption = registry.GetOrAdd(new Option<bool>("--skip-npm-install")
        {
            Description = "Skip running 'npm install' after Node project creation.",
            DefaultValueFactory = _ => false,
        });

        return [NoBundleOption, BundlesChannelOption, SkipNpmInstallOption];
    }

    public async Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);
        cancellationToken.ThrowIfCancellationRequested();

        string root = context.WorkingDirectory.Info.FullName;
        bool force = context.Force;
        bool isTypeScript = string.Equals(context.Language, "typescript", StringComparison.OrdinalIgnoreCase);
        bool noBundle = parseResult.GetValue(NoBundleOption);
        BundleChannel channel = parseResult.GetValue(BundlesChannelOption);
        bool skipNpmInstall = parseResult.GetValue(SkipNpmInstallOption);

        string projectName = ResolveProjectName(context);
        string packageJsonTemplate = isTypeScript
            ? ProjectFiles.ReadTemplate(_assembly, "package-ts.json")
            : ProjectFiles.ReadTemplate(_assembly, "package-js.json");

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "package.json"),
            packageJsonTemplate.Replace(ProjectNamePlaceholder, projectName),
            force);

        if (isTypeScript)
        {
            ProjectFiles.WriteIfMissing(
                Path.Combine(root, "tsconfig.json"),
                ProjectFiles.ReadTemplate(_assembly, "tsconfig.json"),
                force);
        }

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".funcignore"),
            ProjectFiles.ReadTemplate(_assembly, "funcignore"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".gitignore"),
            ProjectFiles.ReadTemplate(_assembly, "gitignore"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "local.settings.json"),
            ProjectFiles.ReadTemplate(_assembly, "local.settings.json"),
            force);

        Directory.CreateDirectory(Path.Combine(root, "src", "functions"));

        // Always lay down the base host.json so the project is valid even
        // with --no-bundle. MergeHostJson below layers extensionBundle on top.
        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "host.json"),
            ProjectFiles.MinimalHostJson,
            force);

        if (!noBundle)
        {
            ProjectFiles.MergeHostJson(
                Path.Combine(root, "host.json"),
                host => EnsureExtensionBundle(host, channel));
        }

        if (!skipNpmInstall)
        {
            // Best-effort. A failure (e.g. npm not installed) leaves the
            // scaffold in place; the user can run `npm install` manually.
            await RunNpmInstall(root, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolveProjectName(InitContext context)
    {
        // Prefer --name; fall back to the directory name. Sanitised to npm-name shape.
        string raw = !string.IsNullOrWhiteSpace(context.ProjectName)
            ? context.ProjectName!
            : context.WorkingDirectory.Info.Name;

        IEnumerable<char> chars = raw.Trim().ToLowerInvariant().Replace(' ', '-')
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
        string sanitized = new([.. chars]);
        return string.IsNullOrEmpty(sanitized) ? "function-app" : sanitized;
    }

    private static void EnsureExtensionBundle(JsonObject host, BundleChannel channel)
    {
        if (host.ContainsKey("extensionBundle"))
        {
            return;
        }

        host["extensionBundle"] = new JsonObject
        {
            ["id"] = ExtensionBundle.IdFor(channel),
            ["version"] = ExtensionBundle.DefaultVersionRange,
        };
    }

    private static async Task<(int ExitCode, string Stderr)> DefaultRunNpmInstall(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            // Windows resolves `npm` via the `npm.cmd` shim, which only works
            // when the launcher is shell-spawned. Direct Process.Start of "npm"
            // fails on Windows otherwise.
            bool isWindows = OperatingSystem.IsWindows();
            var psi = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "npm",
                Arguments = isWindows ? "/c npm install" : "install",
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, "Failed to start 'npm' process.");
            }

            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, stderr);
        }
        catch (Exception ex)
        {
            // 'npm' may not be installed; the scaffolded files are still valid.
            return (-1, ex.Message);
        }
    }
}
