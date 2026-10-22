// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Immutable snapshot of the command context that every template engine operation in one command observes.
/// </summary>
/// <remarks>
/// Commands resolve these values before creating a <see cref="Templater"/>. Template host parameters come only from
/// this snapshot, and a missing project or bundle stays unavailable rather than being inferred.
/// </remarks>
internal sealed record TemplateEngineContext
{
    public TemplateEngineContext(
        WorkingDirectory commandDirectory,
        TemplateEngineProjectContext? project = null,
        TemplateEngineBundleContext? bundle = null,
        bool enableBundleBindingConstraints = false,
        NativeItemOutputIntent itemOutputIntent = NativeItemOutputIntent.LiteralOutput)
    {
        ArgumentNullException.ThrowIfNull(commandDirectory);
        if (itemOutputIntent is not (NativeItemOutputIntent.LiteralOutput or NativeItemOutputIntent.LegacyWorkflowRelative))
        {
            throw new ArgumentOutOfRangeException(nameof(itemOutputIntent));
        }

        CommandDirectory = commandDirectory;
        Project = project;
        Bundle = bundle;
        EnableBundleBindingConstraints = enableBundleBindingConstraints;
        ItemOutputIntent = itemOutputIntent;
    }

    public WorkingDirectory CommandDirectory { get; }

    public bool RequireCleanStore { get; init; }

    public TemplateEngineProjectContext? Project { get; }

    public TemplateEngineBundleContext? Bundle { get; }

    internal bool EnableBundleBindingConstraints { get; }

    public NativeItemOutputIntent ItemOutputIntent { get; }

    internal string ItemOutputIntentValue => ItemOutputIntent == NativeItemOutputIntent.LiteralOutput ? "literal" : "legacy";
}

/// <summary>
/// Resolved Functions project the command targets.
/// </summary>
internal sealed record TemplateEngineProjectContext
{
    public TemplateEngineProjectContext(WorkingDirectory rootDirectory, string? stack, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        if (stack is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        }

        if (language is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(language);
        }

        RootDirectory = rootDirectory;
        Stack = stack;
        Language = language;
    }

    public WorkingDirectory RootDirectory { get; }

    /// <summary>
    /// Project stack, or <c>null</c> when the command did not resolve one.
    /// </summary>
    public string? Stack { get; }

    /// <summary>
    /// Project language, or <c>null</c> when the command did not resolve one.
    /// </summary>
    public string? Language { get; }
}

/// <summary>
/// Resolved extension bundle the project targets.
/// </summary>
internal sealed record TemplateEngineBundleContext
{
    public TemplateEngineBundleContext(string id, string version, string? path = null, TemplateBundleBindingCapabilities? bindings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!NuGetVersion.TryParse(version, out NuGetVersion? resolvedVersion))
        {
            throw new ArgumentException("The resolved bundle version must be a NuGet version, not a version range.", nameof(version));
        }

        Id = id;
        Version = version;
        ResolvedVersion = resolvedVersion;
        if (path is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!System.IO.Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException("The resolved bundle path must be absolute.", nameof(path));
            }
        }

        Path = path is null ? null : System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        Bindings = bindings;
    }

    public string Id { get; }

    public string Version { get; }

    public NuGetVersion ResolvedVersion { get; }

    internal string? Path { get; }

    internal TemplateBundleBindingCapabilities? Bindings { get; }
}
