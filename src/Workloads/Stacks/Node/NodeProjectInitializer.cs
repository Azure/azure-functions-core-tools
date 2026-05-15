// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Scaffolds a Node.js (v4 model) Functions project, branching on <c>--language</c>
/// for JS or TS, and merges the default extension bundle into <c>host.json</c>.
/// </summary>
internal sealed class NodeProjectInitializer : IProjectInitializer
{
    private const string ExtensionBundleId = "Microsoft.Azure.Functions.ExtensionBundle";
    private const string ExtensionBundleVersion = "[4.*, 5.0.0)";
    private const string ProjectNamePlaceholder = "__PROJECT_NAME__";

    public string Stack => "node";

    public IReadOnlyList<string> SupportedLanguages { get; } = ["JavaScript", "TypeScript"];

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
        bool isTypeScript = string.Equals(context.Language, "typescript", StringComparison.OrdinalIgnoreCase);

        string projectName = ResolveProjectName(context);
        string packageJsonTemplate = isTypeScript
            ? ProjectFiles.ReadTemplate("package-ts.json")
            : ProjectFiles.ReadTemplate("package-js.json");

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "package.json"),
            packageJsonTemplate.Replace(ProjectNamePlaceholder, projectName),
            force);

        if (isTypeScript)
        {
            ProjectFiles.WriteIfMissing(
                Path.Combine(root, "tsconfig.json"),
                ProjectFiles.ReadTemplate("tsconfig.json"),
                force);
        }

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".funcignore"),
            ProjectFiles.ReadTemplate("funcignore"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".gitignore"),
            ProjectFiles.ReadTemplate("gitignore"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "local.settings.json"),
            ProjectFiles.ReadTemplate("local.settings.json"),
            force);

        Directory.CreateDirectory(Path.Combine(root, "src", "functions"));

        ProjectFiles.MergeHostJson(
            Path.Combine(root, "host.json"),
            EnsureExtensionBundle);

        return Task.CompletedTask;
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

    private static void EnsureExtensionBundle(JsonObject host)
    {
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
