// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Reflection;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Java;

/// <summary>
/// Scaffolds a Maven-based Java Functions project (<c>pom.xml</c>, a sample HTTP
/// trigger under <c>src/main/java/com/function</c>, <c>host.json</c>,
/// <c>local.settings.json</c>, <c>.gitignore</c>, and <c>.funcignore</c>).
/// </summary>
internal sealed class JavaProjectInitializer : IProjectInitializer
{
    private const string ArtifactIdPlaceholder = "{ArtifactId}";
    private const string FunctionAppNamePlaceholder = "{FunctionAppName}";
    private const string DefaultArtifactId = "azure-functions-java";

    private static readonly Assembly _assembly = typeof(JavaProjectInitializer).Assembly;

    public string Stack => "java";

    public string DisplayName => "Java";

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
        new Dictionary<string, IReadOnlyList<string>>()
        {
            { "Java", [] }
        };

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public Option<bool> NoBundleOption { get; private set; } = default!;

    public Option<BundleChannel> BundlesChannelOption { get; private set; } = default!;

    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        NoBundleOption = registry.GetOrAdd(CommonInitOptions.NoBundle());

        BundlesChannelOption = registry.GetOrAdd(CommonInitOptions.BundlesChannel());

        return [NoBundleOption, BundlesChannelOption];
    }

    public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);
        cancellationToken.ThrowIfCancellationRequested();

        string root = context.WorkingDirectory.Info.FullName;
        bool force = context.Force;
        bool noBundle = parseResult.GetValue(NoBundleOption);
        BundleChannel channel = parseResult.GetValue(BundlesChannelOption);
        string artifactId = ResolveArtifactId(context);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "pom.xml"),
            ProjectFiles.ReadTemplate(_assembly, "pom.xml")
                .Replace(ArtifactIdPlaceholder, artifactId)
                .Replace(FunctionAppNamePlaceholder, artifactId),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "src", "main", "java", "com", "function", "Function.java"),
            ProjectFiles.ReadTemplate(_assembly, "Function.java"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, "local.settings.json"),
            ProjectFiles.ReadTemplate(_assembly, "local.settings.json"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".gitignore"),
            ProjectFiles.ReadTemplate(_assembly, "gitignore"),
            force);

        ProjectFiles.WriteIfMissing(
            Path.Combine(root, ".funcignore"),
            ProjectFiles.ReadTemplate(_assembly, "funcignore"),
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
        // Only fill in when missing so an existing user-customised bundle is preserved.
        // A forced re-init recreates host.json above, so the default bundle is written then.
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

    private static string ResolveArtifactId(InitContext context)
    {
        string raw = !string.IsNullOrWhiteSpace(context.ProjectName)
            ? context.ProjectName!
            : context.WorkingDirectory.Info.Name;

        IEnumerable<char> chars = raw.Trim().ToLowerInvariant().Replace(' ', '-')
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_');
        string sanitized = new([.. chars]);
        return string.IsNullOrEmpty(sanitized) ? DefaultArtifactId : sanitized;
    }
}
