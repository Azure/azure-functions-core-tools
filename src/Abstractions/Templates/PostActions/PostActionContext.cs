// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Describes a workload post action without exposing the underlying template engine.
/// </summary>
public sealed class PostActionContext
{
    /// <summary>
    /// Initializes a snapshot of a template post action and its evaluated outputs.
    /// </summary>
    /// <param name="actionId">The post-action identifier.</param>
    /// <param name="arguments">The action arguments, copied with case-sensitive keys.</param>
    /// <param name="description">The template author's description, if provided.</param>
    /// <param name="manualInstructions">The fallback instructions, if provided.</param>
    /// <param name="continueOnError">Whether execution continues after failure.</param>
    /// <param name="outputBasePath">The template output directory.</param>
    /// <param name="primaryOutputs">The primary output paths relative to the output directory.</param>
    /// <param name="fileChanges">The file changes evaluated by the template engine.</param>
    /// <exception cref="ArgumentNullException">A required collection, output path, or file change is null.</exception>
    /// <exception cref="ArgumentException">The output path or a primary output path is empty or whitespace.</exception>
    public PostActionContext(
        Guid actionId,
        IReadOnlyDictionary<string, string> arguments,
        string? description,
        string? manualInstructions,
        bool continueOnError,
        string outputBasePath,
        IReadOnlyList<string> primaryOutputs,
        IReadOnlyList<PostActionFileChange> fileChanges)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputBasePath);
        ArgumentNullException.ThrowIfNull(primaryOutputs);
        ArgumentNullException.ThrowIfNull(fileChanges);
        foreach (string path in primaryOutputs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(primaryOutputs));
        }

        foreach (PostActionFileChange change in fileChanges)
        {
            ArgumentNullException.ThrowIfNull(change, nameof(fileChanges));
        }

        ActionId = actionId;
        Arguments = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(arguments, StringComparer.Ordinal));
        Description = description;
        ManualInstructions = manualInstructions;
        ContinueOnError = continueOnError;
        OutputBasePath = outputBasePath;
        PrimaryOutputs = Array.AsReadOnly(primaryOutputs.ToArray());
        FileChanges = Array.AsReadOnly(fileChanges.ToArray());
    }

    /// <summary>
    /// Gets the action ID matched to the processor.
    /// </summary>
    public Guid ActionId { get; }

    /// <summary>
    /// Gets the action's arguments with ordinal, case-sensitive keys.
    /// </summary>
    public IReadOnlyDictionary<string, string> Arguments { get; }

    /// <summary>
    /// Gets the template author's description of the action.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the template's fallback instructions, displayed by the CLI when processing fails.
    /// </summary>
    public string? ManualInstructions { get; }

    /// <summary>
    /// Gets whether the template allows the dispatcher to continue after this action fails.
    /// </summary>
    public bool ContinueOnError { get; }

    /// <summary>
    /// Gets the output directory used for template creation.
    /// </summary>
    public string OutputBasePath { get; }

    /// <summary>
    /// Gets the primary output paths relative to <see cref="OutputBasePath"/>.
    /// </summary>
    public IReadOnlyList<string> PrimaryOutputs { get; }

    /// <summary>
    /// Gets the file changes evaluated by the template engine, not a filesystem audit.
    /// </summary>
    public IReadOnlyList<PostActionFileChange> FileChanges { get; }
}
