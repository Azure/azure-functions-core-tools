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
    protected FunctionsProject()
        : this([])
    {
    }

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

    public abstract WorkingDirectory WorkingDirectory { get; }

    /// <summary>
    /// Gets the project's canonical language-stack identifier.
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
    /// Gets the language stack's capability reports, captured when this project was constructed.
    /// An empty collection means no capabilities were reported, not that they are unsupported.
    /// </summary>
    /// <remarks>
    /// The language stack interprets project inputs once when constructing the project. A new observation requires a new instance;
    /// these reports never refresh. Stack, language, and installed package labels alone do not establish support.
    /// </remarks>
    public IReadOnlyList<ProjectCapability> Capabilities { get; }

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
