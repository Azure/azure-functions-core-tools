// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Provides func-owned TemplateEngine components.
/// </summary>
internal static class FuncTemplateComponents
{
    public static IReadOnlyList<(Type Type, IIdentifiedComponent Instance)> Create(TemplateConstraintEvaluationSnapshot snapshot,
        TemplateEngineBundleContext? bundle = null, bool enableBundleBindingConstraints = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        FuncTemplateConstraintEvaluator evaluator = new(new(), enableBundleBindingConstraints);
        List<(Type Type, IIdentifiedComponent Instance)> components =
        [
            (typeof(ITemplateConstraintFactory), new FuncTemplateConstraintFactory("func-workload", evaluator, snapshot, bundle)),
            (typeof(ITemplateConstraintFactory), new FuncTemplateConstraintFactory("func-bundle", evaluator, snapshot, bundle)),
        ];
        if (enableBundleBindingConstraints)
        {
            components.Add((typeof(ITemplateConstraintFactory), new FuncTemplateConstraintFactory("func-bundle-bindings", evaluator, snapshot, bundle)));
        }

        return components.AsReadOnly();
    }
}
