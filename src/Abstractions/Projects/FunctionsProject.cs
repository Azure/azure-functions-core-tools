// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Represents a resolved Azure Functions project with immutable capability reports.
/// </summary>
public abstract class FunctionsProject
{
    /// <summary>
    /// Initializes a project without capability reports.
    /// </summary>
    protected FunctionsProject()
        : this([])
    {
    }

    /// <summary>
    /// Initializes a project with an immutable copy of the language stack's capability reports.
    /// </summary>
    /// <param name="capabilities">The capability reports for this project.</param>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is null.</exception>
    /// <exception cref="ArgumentException">The collection contains a null report.</exception>
    protected FunctionsProject(IEnumerable<ProjectCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ProjectCapability[] captured = [.. capabilities];
        if (captured.Any(capability => capability is null))
        {
            throw new ArgumentException("Project capabilities must not contain null entries.", nameof(capabilities));
        }

        Array.Sort(captured, static (first, second) =>
        {
            int comparison = StringComparer.Ordinal.Compare(first.Id, second.Id);
            return comparison != 0 ? comparison : first.Support.CompareTo(second.Support);
        });
        Capabilities = Array.AsReadOnly(captured);
    }

    /// <summary>
    /// Gets the working directory used to resolve this project.
    /// </summary>
    public abstract WorkingDirectory WorkingDirectory { get; }

    /// <summary>
    /// Gets the project's canonical language-stack identifier.
    /// </summary>
    public abstract string StackName { get; }

    /// <summary>
    /// Gets the language-stack name shown in user-facing output.
    /// </summary>
    public abstract string StackDisplayName { get; }

    /// <summary>
    /// Gets the project's language display name, or null when the stack implies a single language.
    /// </summary>
    public virtual string? Language => null;

    /// <summary>
    /// Gets whether the project can obtain binding extensions from extension bundles.
    /// </summary>
    public abstract bool SupportsExtensionBundles { get; }

    /// <summary>
    /// Gets the reference used to resolve the project's required language worker.
    /// </summary>
    public abstract FunctionsWorkerReference WorkerReference { get; }

    /// <summary>
    /// Gets the language stack's capability reports, captured when this project was constructed.
    /// An empty collection establishes no capability support.
    /// </summary>
    /// <remarks>
    /// The language stack interprets project inputs once when constructing the project. A new observation requires a new instance;
    /// these reports never refresh. Stack, language, and installed package labels alone do not establish support.
    /// </remarks>
    public IReadOnlyList<ProjectCapability> Capabilities { get; }

    /// <summary>
    /// Prepares the project before the Functions Host starts. The default implementation performs no preparation.
    /// </summary>
    /// <param name="context">The host-run settings and reporting services.</param>
    /// <param name="cancellationToken">A token to cancel preparation.</param>
    /// <returns>A task representing project preparation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public virtual Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleans up after a Functions Host run. The default implementation performs no cleanup.
    /// </summary>
    /// <param name="context">The run state and completion outcome.</param>
    /// <param name="cancellationToken">A token to cancel cleanup.</param>
    /// <returns>A task representing project cleanup.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public virtual Task CompleteHostRunAsync(FunctionsProjectHostRunCompletionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
