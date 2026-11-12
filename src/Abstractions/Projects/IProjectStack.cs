// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes an installed stack without project generation behavior.
/// This public contract is implemented by independently shipped workload assemblies.
/// </summary>
public interface IProjectStack
{
    /// <summary>
    /// Gets the canonical stack ID, such as "dotnet".
    /// </summary>
    public string Stack { get; }

    /// <summary>
    /// Gets the stack's label for prompts and help.
    /// </summary>
    public string DisplayName => Stack;

    /// <summary>
    /// Gets additional worker runtime values that resolve to this stack, matched case-insensitively.
    /// </summary>
    public IReadOnlyList<string> WorkerRuntimeAliases => [];

    /// <summary>
    /// Gets canonical language names, such as "C#". A language may belong to multiple stacks.
    /// </summary>
    public IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>
    /// Gets accepted aliases keyed by canonical language name, matched case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; }

    /// <summary>
    /// Runs a template post action for a generated project.
    /// </summary>
    /// <exception cref="NotSupportedException">The stack does not support the requested post action.</exception>
    public Task RunTemplatePostActionAsync(string actionId, WorkingDirectory projectDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException($"The '{Stack}' stack does not support template post-action '{actionId}'.");
    }

    /// <summary>
    /// Runs a template post action with its declared arguments for a generated project.
    /// </summary>
    public Task RunTemplatePostActionAsync(
        string actionId,
        IReadOnlyDictionary<string, string> arguments,
        WorkingDirectory projectDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunTemplatePostActionAsync(actionId, projectDirectory, cancellationToken);
    }
}
