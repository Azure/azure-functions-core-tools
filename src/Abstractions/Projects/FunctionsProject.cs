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
    private static readonly IReadOnlyDictionary<string, string> _emptyTemplateHostParameters =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public abstract WorkingDirectory WorkingDirectory { get; }

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
    /// Gets workload-owned host values available to Microsoft.TemplateEngine bind symbols.
    /// </summary>
    public virtual IReadOnlyDictionary<string, string> TemplateHostParameters => _emptyTemplateHostParameters;

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
