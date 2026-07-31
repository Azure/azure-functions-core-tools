// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Evaluates template constraints (e.g. <c>func-extension-bundle</c>) against
/// the current project so the catalog can hide restricted templates and the
/// scaffolder can surface a restricted template's call-to-action.
/// </summary>
internal interface IFuncTemplateConstraintEvaluator
{
    /// <summary>
    /// Evaluates every template's constraints, returning a result keyed by
    /// <see cref="ITemplateInfo.Identity"/>. Templates with no constraints are
    /// reported as allowed.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="templates"/> is null.</exception>
    public Task<IReadOnlyDictionary<string, TemplateConstraintEvaluation>> EvaluateAsync(
        IReadOnlyList<ITemplateInfo> templates,
        CancellationToken cancellationToken);
}
