// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Reflection;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Scaffolds a Python (v2 model) Functions project and merges the default extension bundle into <c>host.json</c>.
/// </summary>
internal sealed class PythonProjectInitializer : IProjectInitializer
{
    private static readonly Assembly _assembly = typeof(PythonProjectInitializer).Assembly;

    public string Stack => "python";

    public IReadOnlyList<string> SupportedLanguages { get; } = ["Python"];

    public Option<bool> NoBundleOption { get; private set; } = default!;

    public Option<BundleChannel> BundlesChannelOption { get; private set; } = default!;

    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        NoBundleOption = registry.GetOrAdd(CommonInitOptions.NoBundle());

        BundlesChannelOption = registry.GetOrAdd(CommonInitOptions.BundlesChannel());

        return [NoBundleOption, BundlesChannelOption];
    }

    public Task InitializeAsync(
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

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "function_app.py"),
            ProjectFiles.ReadTemplate(_assembly, "function_app.py"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "requirements.txt"),
            ProjectFiles.ReadTemplate(_assembly, "requirements.txt"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "getting_started.md"),
            ProjectFiles.ReadTemplate(_assembly, "getting_started.md"),
            force);

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
}
