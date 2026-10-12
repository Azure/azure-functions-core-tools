// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

internal enum FuncTemplateConstraintOutcome
{
    Eligible,
    Restricted,
    NotEvaluated,
}

internal sealed record TemplateConstraintWorkload(string PackageId, NuGetVersion Version, IReadOnlyCollection<string> Aliases, bool IsUsable, string? PublishedPackageId = null);

internal sealed class FuncTemplateConstraintEvaluator(TemplateConstraintRequirementMatcher matcher)
{
    private readonly TemplateConstraintRequirementMatcher _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));

    public FuncTemplateConstraintOutcome EvaluateAll(IReadOnlyCollection<TemplateConstraintDeclaration> declarations, TemplateConstraintEvaluationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return EvaluateAll(declarations, snapshot.Workloads, snapshot.BundleId, snapshot.BundleVersion);
    }

    public FuncTemplateConstraintOutcome Evaluate(TemplateConstraintDeclaration declaration, TemplateConstraintEvaluationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Evaluate(declaration, snapshot.Workloads, snapshot.BundleId, snapshot.BundleVersion);
    }

    public FuncTemplateConstraintOutcome EvaluateAll(IReadOnlyCollection<TemplateConstraintDeclaration> declarations, IReadOnlyCollection<TemplateConstraintWorkload> workloads, string? bundleId = null, NuGetVersion? bundleVersion = null)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(workloads);
        FuncTemplateConstraintOutcome outcome = FuncTemplateConstraintOutcome.Eligible;
        foreach (TemplateConstraintDeclaration declaration in declarations)
        {
            FuncTemplateConstraintOutcome current = Evaluate(declaration, workloads, bundleId, bundleVersion);
            if (current == FuncTemplateConstraintOutcome.NotEvaluated)
            {
                outcome = current;
            }
            else if (current == FuncTemplateConstraintOutcome.Restricted && outcome == FuncTemplateConstraintOutcome.Eligible)
            {
                outcome = current;
            }
        }

        return outcome;
    }

    public FuncTemplateConstraintOutcome Evaluate(TemplateConstraintDeclaration declaration, IReadOnlyCollection<TemplateConstraintWorkload> workloads, string? bundleId = null, NuGetVersion? bundleVersion = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(workloads);
        if (declaration.Type is not "func-workload" and not "func-bundle")
        {
            return FuncTemplateConstraintOutcome.NotEvaluated;
        }

        if (declaration.Alternatives.Count == 0)
        {
            throw new ArgumentException("A func constraint must have a validated alternative.", nameof(declaration));
        }

        bool satisfied = declaration.Type == "func-workload"
            ? declaration.Alternatives.Any(requirement => workloads.Any(workload => workload.IsUsable
                && _matcher.MatchesWorkload(requirement, workload.PackageId, workload.Version, workload.Aliases, workload.PublishedPackageId)))
            : declaration.Alternatives.Any(requirement => _matcher.MatchesBundle(requirement, bundleId, bundleVersion));
        return satisfied ? FuncTemplateConstraintOutcome.Eligible : FuncTemplateConstraintOutcome.Restricted;
    }
}