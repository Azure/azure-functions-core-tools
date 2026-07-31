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
    /// file the engine wrote, in writing order. <see cref="Modified"/> lists
    /// files an allowlisted post action changed in place (e.g. a
    /// <c>.csproj</c> an add-reference action edited). The renderer surfaces
    /// the created set under <c>Created:</c> and the modified set under
    /// <c>Modified:</c>.
    /// </summary>
    public sealed record Created(IReadOnlyList<string> Files)
        : TemplateApplicationResult
    {
        /// <summary>
        /// Files a post action changed in place rather than created. Empty when
        /// no post action modified an existing file.
        /// </summary>
        public IReadOnlyList<string> Modified { get; init; } = [];

        /// <summary>
        /// Follow-up instructions an allowlisted post action produced (e.g. the
        /// blueprint-registration snippet, or a manual-instructions action's
        /// text). Empty when no post action surfaced guidance. The renderer
        /// prints these after the created/modified file lists.
        /// </summary>
        public IReadOnlyList<string> Messages { get; init; } = [];
    }

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
