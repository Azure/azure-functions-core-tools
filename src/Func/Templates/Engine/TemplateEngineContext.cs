// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using System.Collections.Immutable;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Immutable snapshot of the command context that every Template Engine operation in one command observes.
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
        IReadOnlyDictionary<string, string>? bindings = null)
    {
        ArgumentNullException.ThrowIfNull(commandDirectory);
        CommandDirectory = commandDirectory;
        Project = project;
        Bundle = bundle;
        Bindings = bindings is null
            ? ImmutableDictionary.Create<string, string>(StringComparer.Ordinal)
            : bindings.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public WorkingDirectory CommandDirectory { get; }

    public TemplateEngineProjectContext? Project { get; }

    public TemplateEngineBundleContext? Bundle { get; }

    public IReadOnlyDictionary<string, string> Bindings { get; }
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
    public TemplateEngineBundleContext(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Id = id;
        Version = version;
    }

    public string Id { get; }

    public string Version { get; }
}
