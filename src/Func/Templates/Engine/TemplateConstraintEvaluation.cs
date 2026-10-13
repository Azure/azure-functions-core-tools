// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal enum TemplateConstraintEvaluationStatus
{
    Eligible,
    Restricted,
    NotEvaluated,
    Failed,
}

internal sealed record TemplateConstraintDiagnostic(string Label, string Type, TemplateConstraintEvaluationStatus Status, string Message, string? CallToAction);

internal sealed record TemplateConstraintEvaluation(TemplateConstraintEvaluationStatus Status, IReadOnlyList<TemplateConstraintDiagnostic> Diagnostics)
{
    public static TemplateConstraintEvaluation Eligible { get; } = new(TemplateConstraintEvaluationStatus.Eligible, []);

    public static TemplateConstraintEvaluation NotEvaluated { get; } = new(TemplateConstraintEvaluationStatus.NotEvaluated, []);
}