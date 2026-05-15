// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Scaffolds a Python (v2 model) Functions project and merges the default extension bundle into <c>host.json</c>.
/// </summary>
internal sealed class PythonProjectInitializer : IProjectInitializer
{
    private const string ExtensionBundleId = "Microsoft.Azure.Functions.ExtensionBundle";
    private const string ExtensionBundleVersion = "[4.*, 5.0.0)";

    public string Stack => "python";

    public IReadOnlyList<string> SupportedLanguages { get; } = ["Python"];

    public IReadOnlyList<Option> GetInitOptions() => [];

    public Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string root = context.WorkingDirectory.Info.FullName;
        bool force = context.Force;

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "function_app.py"),
            ProjectFiles.ReadTemplate("function_app.py"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "requirements.txt"),
            ProjectFiles.ReadTemplate("requirements.txt"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "getting_started.md"),
            ProjectFiles.ReadTemplate("getting_started.md"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".gitignore"),
            ProjectFiles.ReadTemplate("gitignore"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "local.settings.json"),
            ProjectFiles.ReadTemplate("local.settings.json"),
            force);

        ProjectFiles.MergeHostJson(
            Path.Combine(root, "host.json"),
            EnsureExtensionBundle);

        return Task.CompletedTask;
    }

    private static void EnsureExtensionBundle(JsonObject host)
    {
        // Only fill in when missing so a user-customised bundle survives `--force`.
        if (host.ContainsKey("extensionBundle"))
        {
            return;
        }

        host["extensionBundle"] = new JsonObject
        {
            ["id"] = ExtensionBundleId,
            ["version"] = ExtensionBundleVersion,
        };
    }
}

