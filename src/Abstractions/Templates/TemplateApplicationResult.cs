// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Outcome of <see cref="ITemplateEngineProvider.ApplyAsync"/>. Closed
/// discriminated union over success, "already exists" (no <c>--force</c>),
/// and explicit failure.
/// </summary>
public abstract record TemplateApplicationResult
{
    private TemplateApplicationResult()
    {
    }

    /// <summary>
    /// The template materialised successfully. <see cref="Files"/> lists every
    /// file the engine wrote, in writing order. Renderer surfaces the list to
    /// the user.
    /// </summary>
    public sealed record Created(IReadOnlyList<string> Files)
        : TemplateApplicationResult;

    /// <summary>
    /// One or more files the template would have written already exist and
    /// <c>--force</c> was not set. The runner exits non-zero with a
    /// "use <c>--force</c>" hint.
    /// </summary>
    public sealed record AlreadyExists(IReadOnlyList<string> ExistingFiles)
        : TemplateApplicationResult;

    /// <summary>
    /// The application failed for a named reason. The runner dispatches on
    /// <see cref="Failure"/> to render the appropriate hint and exit code.
    /// </summary>
    public sealed record Failed(TemplateApplicationFailure Failure)
        : TemplateApplicationResult;
}
