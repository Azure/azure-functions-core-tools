// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Edge;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Evaluates template constraints through the engine's
/// <see cref="TemplateConstraintManager"/>, treating any evaluation or
/// initialisation failure as a restriction so a broken constraint hides its
/// template rather than crashing the catalog.
/// </summary>
internal sealed class FuncTemplateConstraintEvaluator(IFuncTemplateEngineSession session) : IFuncTemplateConstraintEvaluator
{
    private readonly IFuncTemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, TemplateConstraintEvaluation>> EvaluateAsync(
        IReadOnlyList<ITemplateInfo> templates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templates);

        var results = new Dictionary<string, TemplateConstraintEvaluation>(StringComparer.Ordinal);
        if (templates.Count == 0)
        {
            return results;
        }

        using var manager = new TemplateConstraintManager(_session.Settings);
        IReadOnlyList<(ITemplateInfo Template, IReadOnlyList<TemplateConstraintResult> Result)> evaluated =
            await manager.EvaluateConstraintsAsync(templates, cancellationToken);

        foreach ((ITemplateInfo template, IReadOnlyList<TemplateConstraintResult> constraintResults) in evaluated)
        {
            results[template.Identity] = Reduce(constraintResults);
        }

        return results;
    }

    private static TemplateConstraintEvaluation Reduce(IReadOnlyList<TemplateConstraintResult> constraintResults)
    {
        foreach (TemplateConstraintResult result in constraintResults)
        {
            if (result.EvaluationStatus != TemplateConstraintResult.Status.Allowed &&
                result.EvaluationStatus != TemplateConstraintResult.Status.NotEvaluated)
            {
                return new TemplateConstraintEvaluation(false, result.LocalizedErrorMessage, result.CallToAction);
            }
        }

        return TemplateConstraintEvaluation.Allowed;
    }
}
