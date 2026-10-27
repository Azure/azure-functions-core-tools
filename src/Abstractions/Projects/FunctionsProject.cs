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

    /// <summary>
    /// Gets the canonical stack identifier used to bind project-capability reports to this resolved project.
    /// </summary>
    public abstract string StackName { get; }

    public abstract string StackDisplayName { get; }

    /// <summary>
    /// Display name of the project's language (e.g. "C#", "TypeScript"), or
    /// <c>null</c> for single-language stacks where the stack implies it.
    /// </summary>
    public virtual string? Language => null;

    public abstract bool SupportsExtensionBundles { get; }

    public abstract FunctionsWorkerReference WorkerReference { get; }

    /// <summary>
    /// Captures the stack implementation's current capability reports without modifying the project.
    /// Returns null when the stack cannot provide a snapshot.
    /// </summary>
    /// <remarks>
    /// Implementations must inspect the actual project on each call and fingerprint all inputs used to establish facts.
    /// Stack, language, and installed package versions alone are not authoritative semantic evidence.
    /// </remarks>
    public virtual ValueTask<ProjectFacts?> GetProjectFactsAsync(ProjectFactsContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<ProjectFacts?>(null);
    }

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
