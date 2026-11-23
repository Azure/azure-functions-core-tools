// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// A post action to process and the template creation that produced it.
/// </summary>
internal sealed record PostActionContext
{
    public PostActionContext(
        IEngineEnvironmentSettings environment,
        IPostAction action,
        ICreationEffects creationEffects,
        ICreationResult creationResult,
        string outputBasePath)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(creationEffects);
        ArgumentNullException.ThrowIfNull(creationResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputBasePath);

        Environment = environment;
        Action = action;
        CreationEffects = creationEffects;
        CreationResult = creationResult;
        OutputBasePath = outputBasePath;
    }

    public IEngineEnvironmentSettings Environment { get; }

    public IPostAction Action { get; }

    public ICreationEffects CreationEffects { get; }

    public ICreationResult CreationResult { get; }

    /// <summary>
    /// The directory the template was instantiated into.
    /// </summary>
    public string OutputBasePath { get; }
}
