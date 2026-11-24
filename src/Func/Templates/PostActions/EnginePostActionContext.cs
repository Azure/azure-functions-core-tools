// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// A post action to process and the template creation that produced it.
/// </summary>
internal sealed record EnginePostActionContext
{
    /// <summary>
    /// Initializes the engine context for a post action.
    /// </summary>
    /// <param name="environment">The template engine environment.</param>
    /// <param name="action">The action to process.</param>
    /// <param name="creationEffects">The evaluated creation effects.</param>
    /// <param name="creationResult">The template creation result.</param>
    /// <param name="outputBasePath">The template output directory.</param>
    /// <param name="template">The template that produced the action.</param>
    public EnginePostActionContext(
        IEngineEnvironmentSettings environment,
        IPostAction action,
        ICreationEffects creationEffects,
        ICreationResult creationResult,
        string outputBasePath,
        ITemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(creationEffects);
        ArgumentNullException.ThrowIfNull(creationResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputBasePath);
        ArgumentNullException.ThrowIfNull(template);

        Environment = environment;
        Action = action;
        CreationEffects = creationEffects;
        CreationResult = creationResult;
        OutputBasePath = outputBasePath;
        Template = template;
    }

    /// <summary>
    /// Gets the template engine environment.
    /// </summary>
    public IEngineEnvironmentSettings Environment { get; }

    /// <summary>
    /// Gets the action to process.
    /// </summary>
    public IPostAction Action { get; }

    /// <summary>
    /// Gets the evaluated creation effects.
    /// </summary>
    public ICreationEffects CreationEffects { get; }

    /// <summary>
    /// Gets the template creation result.
    /// </summary>
    public ICreationResult CreationResult { get; }

    /// <summary>
    /// The directory the template was instantiated into.
    /// </summary>
    public string OutputBasePath { get; }

    /// <summary>
    /// Gets the template that produced the action.
    /// </summary>
    public ITemplateInfo Template { get; }
}
