// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplaterFactory(ITemplateStore store, IFileSystem fileSystem) : ITemplaterFactory
{
    private readonly ITemplateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<Templater> CreateAsync(TemplateEngineContext context, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        TemplateEngineSession session = await _store.OpenReadAsync(context, cancellationToken, waiting: waiting);
        return new(session, new(session, _fileSystem));
    }

    public async Task<Templater> CreateAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
        CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        TemplateEngineSession session = await _store.OpenReadAsync(context, cancellationToken, snapshot, waiting: waiting);
        return new(session, new(session, _fileSystem));
    }

    public async Task<Templater> CreateProjectAsync(TemplateEngineContext context, TemplateConstraintEvaluationSnapshot snapshot,
        TemplateProjectSupport projectSupport, CancellationToken cancellationToken, Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(projectSupport);
        TemplateEngineSession session = await _store.OpenReadAsync(context, cancellationToken, snapshot, waiting: waiting);
        return new(session, new(session, _fileSystem, projectSupport));
    }
}
