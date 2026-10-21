// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Distinguishes satisfied constraints from restrictions, unknown constraints and evaluation failures.
/// </summary>
internal enum TemplateConstraintEvaluationStatus
{
    Eligible,
    Restricted,
    NotEvaluated,
    Failed,
}

/// <summary>
/// Explains an unsatisfied constraint and any action the user can take.
/// </summary>
internal sealed record TemplateConstraintDiagnostic(string Label, string Type, TemplateConstraintEvaluationStatus Status, string Message, string? CallToAction);

/// <summary>
/// Combines constraint eligibility and diagnostics for a captured command context.
/// </summary>
internal sealed record TemplateConstraintEvaluation(TemplateConstraintEvaluationStatus Status, IReadOnlyList<TemplateConstraintDiagnostic> Diagnostics)
{
    public static TemplateConstraintEvaluation Eligible { get; } = new(TemplateConstraintEvaluationStatus.Eligible, []);

    public static TemplateConstraintEvaluation NotEvaluated { get; } = new(TemplateConstraintEvaluationStatus.NotEvaluated, []);
}