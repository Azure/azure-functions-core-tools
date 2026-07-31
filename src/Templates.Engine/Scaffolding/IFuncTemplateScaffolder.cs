// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Materializes a chosen template into the working directory. Runs the engine
/// dry-run first (conflicting existing files without <c>--force</c> map to
/// <see cref="TemplateApplicationResult.AlreadyExists"/>), then instantiates
/// and dispatches allowlisted post actions. Expected failures come back as
/// <see cref="TemplateApplicationResult.Failed"/> rather than exceptions.
/// </summary>
internal interface IFuncTemplateScaffolder
{
    /// <summary>
    /// Applies the template resolved in <paramref name="context"/> using the
    /// stage-B <paramref name="parseResult"/> for per-template option values.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="parseResult"/> is null.</exception>
    public Task<TemplateApplicationResult> ApplyAsync(NewContext context, ParseResult parseResult, CancellationToken cancellationToken);
}
