// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal interface ITemplateStore
{
    public Task<TemplateEngineSession> OpenReadAsync(TemplateEngineContext context, CancellationToken cancellationToken,
        TemplateConstraintEvaluationSnapshot? snapshot = null, TemplateProjectSupport? projectSupport = null,
        Action<TemplateStoreWaitReason>? waiting = null);

    public Task<TemplateStoreWriteSession> OpenWriteAsync(TemplateEngineContext context, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason>? waiting = null);
}
