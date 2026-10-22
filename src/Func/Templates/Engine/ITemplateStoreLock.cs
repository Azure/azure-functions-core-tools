// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal enum TemplateStoreAccess
{
    Read,
    Write,
}

internal enum TemplateStoreWaitReason
{
    ActiveReaders,
    LifecycleOperation,
}

/// <summary>
/// Protects cooperating CLI processes. Ownership lasts until disposal or process exit.
/// </summary>
internal interface ITemplateStoreLock
{
    public Task<IDisposable> AcquireAsync(string directory, TemplateStoreAccess access, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason>? waiting = null);
}
