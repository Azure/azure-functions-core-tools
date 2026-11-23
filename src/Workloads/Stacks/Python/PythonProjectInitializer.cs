// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Scaffolds a Python (v2 model) Functions project and merges the default extension bundle into <c>host.json</c>.
/// </summary>
internal sealed partial class PythonProjectInitializer(PythonProjectStack stack) : IProjectInitializer
{
    internal const string PackageManagerOptionName = "--package-manager";
    internal const string UvValue = "uv";
    internal const string PipValue = "pip";
    internal const string DefaultProjectName = "function-app";

    // Lower bound written to `requires-python`; the `.python-version` pin is platform-specific
    // (see PythonSupportedVersions).
    internal const string MinimumPythonVersion = "3.10";

    private static readonly Assembly _assembly = typeof(PythonProjectInitializer).Assembly;

    private readonly PythonProjectStack _stack = stack ?? throw new ArgumentNullException(nameof(stack));

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases => _stack.SupportedLanguageAliases;

    public string Stack => _stack.Stack;

    public string DisplayName => _stack.DisplayName;

    public IReadOnlyList<string> WorkerRuntimeAliases => _stack.WorkerRuntimeAliases;

    public IReadOnlyList<string> SupportedLanguages => _stack.SupportedLanguages;

    public Option<bool> NoBundleOption { get; private set; } = default!;

    public Option<BundleChannel> BundlesChannelOption { get; private set; } = default!;

    public Option<string> PackageManagerOption { get; private set; } = default!;

    // Seam so tests control whether uv appears to be installed.
    internal Func<string?> FindUv { get; set; } = UvLocator.Find;

    // Seam so tests control the platform the `.python-version` pin is chosen for.
    internal Func<string> GetRuntimeIdentifier { get; set; } = PythonSupportedVersions.GetCurrentRuntimeIdentifier;

    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        NoBundleOption = registry.GetOrAdd(CommonInitOptions.NoBundle());

        BundlesChannelOption = registry.GetOrAdd(CommonInitOptions.BundlesChannel());

        Option<string> packageManager = new(PackageManagerOptionName)
        {
            Description = $"Python package manager: {UvValue} or {PipValue} (default: {UvValue} if installed, otherwise {PipValue}).",
        };
        packageManager.AcceptOnlyFromAmong(UvValue, PipValue);
        PackageManagerOption = registry.GetOrAdd(packageManager);

        return [NoBundleOption, BundlesChannelOption, PackageManagerOption];
    }

    public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);
        cancellationToken.ThrowIfCancellationRequested();

        string language = NormalizeLanguage(context.Language);
        if (!SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Language '{context.Language}' is not supported. Supported languages: {string.Join(", ", SupportedLanguages)}.", nameof(context));
        }

        string root = context.WorkingDirectory.Info.FullName;
        bool force = context.Force;
        bool noBundle = parseResult.GetValue(NoBundleOption);
        BundleChannel channel = parseResult.GetValue(BundlesChannelOption);
        bool useUv = ResolvePackageManager(parseResult.GetValue(PackageManagerOption)) == PythonPackageManager.Uv;

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "function_app.py"),
            ProjectFiles.ReadTemplate(_assembly, "function_app.py"),
            force);

        if (useUv)
        {
            // uv owns the environment; requirements.txt is exported from uv.lock on `func run`.
            ProjectFiles.WriteIfMissing(
                Path.Combine(root, PythonPackageManagerDetector.PyProjectFileName),
                ProjectFiles.ReadTemplate(_assembly, "pyproject.toml")
                    .Replace("{{ProjectName}}", SanitizeProjectName(context.WorkingDirectory.Info.Name), StringComparison.Ordinal)
                    .Replace("{{MinimumPythonVersion}}", MinimumPythonVersion, StringComparison.Ordinal),
                force);

            // The worker payload may not be installed yet, so pin from the per-RID fallback table.
            string pythonVersion = PythonSupportedVersions.GetDefault(PythonSupportedVersions.GetFallback(GetRuntimeIdentifier()));
            ProjectFiles.WriteIfMissing(
                Path.Combine(root, ".python-version"),
                ProjectFiles.ReadTemplate(_assembly, "python-version")
                    .Replace("{{DefaultPythonVersion}}", pythonVersion, StringComparison.Ordinal),
                force);

            ProjectFiles.WriteIfMissing(
                Path.Combine(root, "getting_started.md"),
                ProjectFiles.ReadTemplate(_assembly, "getting_started.uv.md"),
                force);
        }
        else
        {
            ProjectFiles.WriteIfMissing(
                Path.Combine(root, PythonPackageManagerDetector.RequirementsFileName),
                ProjectFiles.ReadTemplate(_assembly, "requirements.txt"),
                force);

            ProjectFiles.WriteIfMissing(
                Path.Combine(root, "getting_started.md"),
                ProjectFiles.ReadTemplate(_assembly, "getting_started.md"),
                force);
        }

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".gitignore"),
            ProjectFiles.ReadTemplate(_assembly, "gitignore"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "local.settings.json"),
            ProjectFiles.ReadTemplate(_assembly, "local.settings.json"),
            force);

        // Always lay down the base host.json so the project is valid even
        // with --no-bundles. MergeHostJson below layers extensionBundle on top.
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

        return Task.CompletedTask;
    }

    private PythonPackageManager ResolvePackageManager(string? requested)
    {
        if (string.Equals(requested, UvValue, StringComparison.OrdinalIgnoreCase))
        {
            return PythonPackageManager.Uv;
        }

        if (string.Equals(requested, PipValue, StringComparison.OrdinalIgnoreCase))
        {
            return PythonPackageManager.Pip;
        }

        return FindUv() is not null ? PythonPackageManager.Uv : PythonPackageManager.Pip;
    }

    // PEP 508 names: letters, digits, '.', '_', '-', starting and ending alphanumeric.
    internal static string SanitizeProjectName(string? directoryName)
    {
        string lowered = (directoryName ?? string.Empty).ToLowerInvariant();
        string name = InvalidNameCharsRegex().Replace(lowered, "-").Trim('-', '.', '_');
        return name.Length == 0 ? DefaultProjectName : name;
    }

    [GeneratedRegex(@"[^a-z0-9._-]+")]
    private static partial Regex InvalidNameCharsRegex();

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

    internal string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "Python";
        }

        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in SupportedLanguageAliases)
        {
            if (string.Equals(entry.Key, language, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Key;
            }

            if (entry.Value.Contains(language, StringComparer.OrdinalIgnoreCase))
            {
                return entry.Key;
            }
        }

        return language;
    }
}
