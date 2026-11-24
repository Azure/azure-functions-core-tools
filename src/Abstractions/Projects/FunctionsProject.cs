// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Represents a resolved Azure Functions project.
/// </summary>
public abstract class FunctionsProject
{
    public abstract WorkingDirectory WorkingDirectory { get; }

    public abstract IProjectStack ProjectStack { get; }

    /// <summary>
    /// Gets the detected language, or <c>null</c> if the project does not distinguish it.
    /// </summary>
    public virtual LanguageDescriptor? Language => null;

    public abstract bool SupportsExtensionBundles { get; }

    public abstract FunctionsWorkerReference WorkerReference { get; }

    public virtual Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public virtual Task CompleteHostRunAsync(FunctionsProjectHostRunCompletionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
