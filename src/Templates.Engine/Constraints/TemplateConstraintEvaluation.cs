// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// The outcome of evaluating a template's constraints for the current project.
/// </summary>
/// <param name="IsAllowed">True when every constraint is satisfied.</param>
/// <param name="ErrorMessage">Why the template is restricted, or <c>null</c> when allowed.</param>
/// <param name="CallToAction">What the user can change to make it available, or <c>null</c>.</param>
internal sealed record TemplateConstraintEvaluation(bool IsAllowed, string? ErrorMessage, string? CallToAction)
{
    /// <summary>
    /// A satisfied evaluation with no restriction reason.
    /// </summary>
    internal static TemplateConstraintEvaluation Allowed { get; } = new(true, null, null);

    /// <summary>
    /// Composes the user-facing restriction message from the error and, when
    /// present, the call-to-action.
    /// </summary>
    internal string ToRestrictionMessage() =>
        string.IsNullOrWhiteSpace(CallToAction)
            ? ErrorMessage ?? "This template is not available for the current project."
            : $"{ErrorMessage} {CallToAction}";
}
