// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Supplies the complete planning and persistence support for internal project-template invocation.
/// </summary>
internal sealed record TemplateProjectSupport(
    TemplateProjectConfigurationPlanner Planner, TemplateProjectConfigurationWriter Writer)
{
    public TemplateProjectConfigurationPlanner Planner { get; } = Planner ?? throw new ArgumentNullException(nameof(Planner));

    public TemplateProjectConfigurationWriter Writer { get; } = Writer ?? throw new ArgumentNullException(nameof(Writer));

    public DotNetProjectLayoutQualification? DotNetLayout { get; init; }

    public Func<NativeProjectTemplateSource, CancellationToken, Task<DotNetProjectLayoutQualification?>>? QualifyLayoutAsync { get; init; }
}
