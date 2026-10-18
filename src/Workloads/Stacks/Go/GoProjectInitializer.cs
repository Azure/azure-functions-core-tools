// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Reflection;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Go;

/// <summary>
/// Scaffolds a Go Functions project (<c>go.mod</c>, <c>main.go</c>, <c>.funcignore</c>, <c>.gitignore</c>,
/// <c>local.settings.json</c>) targeting the Go worker SDK, then optionally runs <c>go mod tidy</c>.
/// </summary>
internal sealed class GoProjectInitializer(GoProjectStack stack) : IProjectInitializer
{
    private const string ModuleNamePlaceholder = "{ModuleName}";
    private const string DefaultModuleName = "app";
    private static readonly Assembly _assembly = typeof(GoProjectInitializer).Assembly;
    private readonly GoProjectStack _stack = stack ?? throw new ArgumentNullException(nameof(stack));

    // Internal seam so tests can stub out the `go mod tidy` invocation
    // without spawning real processes.
    internal Func<string, CancellationToken, Task<(int ExitCode, string Stderr)>> RunGoModTidy { get; set; } = DefaultRunGoModTidy;

    public string Stack => _stack.Stack;

    public string DisplayName => _stack.DisplayName;

    public IReadOnlyList<string> WorkerRuntimeAliases => _stack.WorkerRuntimeAliases;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases => _stack.SupportedLanguageAliases;

    public IReadOnlyList<string> SupportedLanguages => _stack.SupportedLanguages;

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

    public async Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
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
            // go.mod intentionally omits a require line for azure-functions-golang-worker;
            // `go mod tidy` (here on init, and again on every `func start`) resolves the
            // latest available tag from the imports in main.go, so users pick up new SDK
            // releases without contributors having to bump a pinned version.
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
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        string sanitized = new([.. chars]);
        bool reserved = sanitized is "go" or "toolchain" or "con" or "prn" or "aux" or "nul"
            || (sanitized.Length == 4 && sanitized[..3] is "com" or "lpt" && sanitized[3] is >= '1' and <= '9');
        return string.IsNullOrEmpty(sanitized) || sanitized.StartsWith('-') || reserved ? DefaultModuleName : sanitized;
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

    internal static Task<(int ExitCode, string Stderr)> DefaultRunGoModTidy(string workingDirectory, CancellationToken cancellationToken)
        => new GoProjectPreparation().RunGoModTidyAsync(workingDirectory, cancellationToken);
}
