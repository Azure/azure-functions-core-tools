// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Creates the command-scoped <see cref="Templater"/> for a resolved <see cref="TemplateEngineContext"/>.
/// </summary>
internal interface ITemplaterFactory
{
    /// <summary>
    /// Creates a templater bound to <paramref name="context"/>. The caller disposes it when the command is finished.
    /// </summary>
    public Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason>? waiting = null);

    public Task<Templater> CreateAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
        CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null);

    /// <summary>
    /// Creates an internal project-enabled flow; production commands and DI do not activate this staged path yet.
    /// </summary>
    /// <exception cref="ArgumentNullException">The context, availability snapshot or paired project support is null.</exception>
    public Task<Templater> CreateProjectAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
        TemplateProjectSupport projectSupport, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null);
}
