// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

internal enum FuncTemplateConstraintOutcome
{
    Eligible,
    Restricted,
    NotEvaluated,
    Failed,
}

internal sealed record TemplateConstraintWorkload(string PackageId, NuGetVersion Version, IReadOnlyCollection<string> Aliases, bool IsUsable, string? PublishedPackageId = null);

internal sealed class FuncTemplateConstraintEvaluator(TemplateConstraintRequirementMatcher matcher, bool enableBundleBindingConstraints = false)
{
    private readonly TemplateConstraintRequirementMatcher _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));

    public FuncTemplateConstraintOutcome EvaluateAll(IReadOnlyCollection<TemplateConstraintDeclaration> declarations, TemplateConstraintEvaluationSnapshot snapshot, TemplateEngineBundleContext? bundle = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return EvaluateAllCore(declarations, snapshot.Workloads, bundle?.Id, bundle?.ResolvedVersion, bundle);
    }

    public FuncTemplateConstraintOutcome Evaluate(TemplateConstraintDeclaration declaration, TemplateConstraintEvaluationSnapshot snapshot, TemplateEngineBundleContext? bundle = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return EvaluateCore(declaration, snapshot.Workloads, bundle?.Id, bundle?.ResolvedVersion, bundle);
    }

    public FuncTemplateConstraintOutcome EvaluateAll(IReadOnlyCollection<TemplateConstraintDeclaration> declarations, IReadOnlyCollection<TemplateConstraintWorkload> workloads, string? bundleId = null, NuGetVersion? bundleVersion = null)
        => EvaluateAllCore(declarations, workloads, bundleId, bundleVersion, null);

    public FuncTemplateConstraintOutcome Evaluate(TemplateConstraintDeclaration declaration, IReadOnlyCollection<TemplateConstraintWorkload> workloads, string? bundleId = null, NuGetVersion? bundleVersion = null)
        => EvaluateCore(declaration, workloads, bundleId, bundleVersion, null);

    internal (FuncTemplateConstraintOutcome Outcome, string? Diagnostic) EvaluateBundleBindings(
        TemplateConstraintDeclaration declaration, TemplateEngineBundleContext? bundle)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (!enableBundleBindingConstraints || declaration.Type != "func-bundle-bindings")
        {
            return (FuncTemplateConstraintOutcome.NotEvaluated, "Bundle binding constraints are not enabled in this template flow.");
        }

        IReadOnlyList<string> required = declaration.RequiredBindings
            ?? throw new ArgumentException("A binding constraint must have validated requirements.", nameof(declaration));
        if (required.Count == 0)
        {
            // Explicitly dependency-free templates need no extension metadata, including in no-bundle projects.
            return (FuncTemplateConstraintOutcome.Eligible, null);
        }

        if (bundle is null)
        {
            return (FuncTemplateConstraintOutcome.Restricted, "This template requires bindings from a resolved extension bundle.");
        }

        if (bundle.Bindings is not { } capabilities || !capabilities.MatchesSource(bundle))
        {
            return (FuncTemplateConstraintOutcome.Failed,
                "Binding capabilities were not captured for the selected bundle identity, version and path. Resolve that bundle's local metadata and start a new template flow.");
        }

        if (capabilities.Diagnostic is { } diagnostic)
        {
            return (FuncTemplateConstraintOutcome.Failed, diagnostic);
        }

        string[] missing = [.. required.Where(binding => !capabilities.Contains(binding))];
        return missing.Length == 0
            ? (FuncTemplateConstraintOutcome.Eligible, null)
            : (FuncTemplateConstraintOutcome.Restricted, $"The selected bundle '{bundle.Id}' {bundle.Version} does not supply required bindings: {string.Join(", ", missing)}.");
    }

    private FuncTemplateConstraintOutcome EvaluateAllCore(IReadOnlyCollection<TemplateConstraintDeclaration> declarations,
        IReadOnlyCollection<TemplateConstraintWorkload> workloads, string? bundleId, NuGetVersion? bundleVersion, TemplateEngineBundleContext? bundle)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(workloads);
        FuncTemplateConstraintOutcome outcome = FuncTemplateConstraintOutcome.Eligible;
        foreach (TemplateConstraintDeclaration declaration in declarations)
        {
            FuncTemplateConstraintOutcome current = EvaluateCore(declaration, workloads, bundleId, bundleVersion, bundle);
            if (current > outcome)
            {
                outcome = current;
            }
        }

        return outcome;
    }

    private FuncTemplateConstraintOutcome EvaluateCore(TemplateConstraintDeclaration declaration,
        IReadOnlyCollection<TemplateConstraintWorkload> workloads, string? bundleId, NuGetVersion? bundleVersion, TemplateEngineBundleContext? bundle)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(workloads);
        if (declaration.Type == "func-bundle-bindings")
        {
            return EvaluateBundleBindings(declaration, bundle).Outcome;
        }

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