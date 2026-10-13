// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplaterFactory(ITemplateStore store) : ITemplaterFactory
{
    private readonly ITemplateStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new(await _store.OpenReadAsync(context, cancellationToken, waiting: waiting));
    }

    public async Task<Templater> CreateAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
        CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(await _store.OpenReadAsync(context, cancellationToken, snapshot, waiting: waiting));
    }

    public async Task<Templater> CreateProjectAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
        TemplateProjectConfigurationPlanner planner, TemplateProjectConfigurationWriter writer, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(writer);
        return new(await _store.OpenReadAsync(context, cancellationToken, snapshot, planner, writer, waiting));
    }
}
