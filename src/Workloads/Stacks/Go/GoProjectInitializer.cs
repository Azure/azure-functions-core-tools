// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Go;

/// <summary>
/// Scaffolds a Go Functions project (<c>go.mod</c>, <c>main.go</c>, <c>.funcignore</c>, <c>.gitignore</c>,
/// <c>local.settings.json</c>) targeting the Go worker SDK, then optionally runs <c>go mod tidy</c>.
/// </summary>
internal sealed class GoProjectInitializer : IProjectInitializer
{
    private const string ModuleNamePlaceholder = "{ModuleName}";
    private const string DefaultModuleName = "app";
    private static readonly Assembly _assembly = typeof(GoProjectInitializer).Assembly;

    // Internal seam so tests can stub out the `go mod tidy` invocation
    // without spawning real processes.
    internal Func<string, CancellationToken, Task<(int ExitCode, string Stderr)>> RunGoModTidy { get; set; } = DefaultRunGoModTidy;

    public string Stack => "go";

    public string DisplayName => "Go";

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
        new Dictionary<string, IReadOnlyList<string>>()
        {
            { "Go", ["golang"] }
        };

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public Option<bool> SkipGoModTidyOption { get; private set; } = default!;

    public Option<bool> NoBundleOption { get; private set; } = default!;

    public Option<BundleChannel> BundlesChannelOption { get; private set; } = default!;

    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        SkipGoModTidyOption = registry.GetOrAdd(new Option<bool>("--skip-go-mod-tidy")
        {
            Description = "Skip running 'go mod tidy' after Go project creation.",
            DefaultValueFactory = _ => false,
        });

        NoBundleOption = registry.GetOrAdd(CommonInitOptions.NoBundle());

        BundlesChannelOption = registry.GetOrAdd(CommonInitOptions.BundlesChannel());

        return [SkipGoModTidyOption, NoBundleOption, BundlesChannelOption];
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
        bool noBundle = parseResult.GetValue(NoBundleOption);
        BundleChannel channel = parseResult.GetValue(BundlesChannelOption);
        string moduleName = ResolveModuleName(context);

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

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "go.mod"),
            // TODO: revisit how the Go worker version is pinned. Go modules
            // don't support relaxed ranges (e.g. `1.x`), so today we hard-pin
            // a preview version in the template. Once the worker hits GA, we
            // should explore a "stay current" strategy, e.g. running
            // `go get github.com/azure/azure-functions-golang-worker@v0`
            // after scaffold so users always get the newest line.
            ProjectFiles.ReadTemplate(_assembly, "go.mod").Replace(ModuleNamePlaceholder, moduleName),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "main.go"),
            ProjectFiles.ReadTemplate(_assembly, "main.go"),
            force);

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

        if (!parseResult.GetValue(SkipGoModTidyOption))
        {
            await RunGoModTidy(root, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolveModuleName(InitContext context)
    {
        string raw = !string.IsNullOrWhiteSpace(context.ProjectName)
            ? context.ProjectName!
            : context.WorkingDirectory.Info.Name;

        IEnumerable<char> chars = raw.Trim().ToLowerInvariant().Replace(' ', '-')
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_');
        string sanitized = new([.. chars]);
        return string.IsNullOrEmpty(sanitized) ? DefaultModuleName : sanitized;
    }

    private static void EnsureExtensionBundle(JsonObject host, BundleChannel channel)
    {
        // Only fill in when missing so a user-customised bundle survives `--force`.
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

    private static async Task<(int ExitCode, string Stderr)> DefaultRunGoModTidy(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("go", "mod tidy")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, "Failed to start 'go' process.");
            }

            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, stderr);
        }
        catch (Exception ex)
        {
            // 'go' may not be installed; the scaffolded files are still valid.
            return (-1, ex.Message);
        }
    }
}
