// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// A flattened, render-ready view of an installed workload. Aliases are
/// derived from the worker-runtime values declared by the workload's
/// <see cref="IProjectInitializer"/> and <see cref="ITemplateProvider"/>
/// contributions, so they can never drift from what the commands actually
/// match against.
/// </summary>
public sealed record WorkloadSummary(
    string PackageId,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases);
