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

    public abstract string StackName { get; }

    public abstract string StackDisplayName { get; }

    /// <summary>
    /// Display name of the project's language (e.g. "C#", "TypeScript",
    /// "Python"). Defaults to <see cref="StackName"/> for single-language
    /// stacks where the stack implies the language; multi-language stacks
    /// (e.g. .NET, Node) override it with the specific language. Always
    /// non-null so template selection can resolve a language for every project.
    /// </summary>
    public virtual string Language => StackName;

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
