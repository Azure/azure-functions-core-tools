// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Inputs to <see cref="IProjectInitializer.InitializeAsync"/>. Resolved
/// values for the built-in <c>func init</c> options, layered on top of the
/// shared <see cref="WorkloadContext"/>. Workload-specific options live on
/// the <c>ParseResult</c> the initializer also receives.
/// </summary>
/// <param name="ProjectPath">The project directory.</param>
/// <param name="ProjectName">Project name (from <c>--name</c> or the directory name).</param>
/// <param name="Language">Programming language (from <c>--language</c> or a prompt). May be null.</param>
/// <param name="Force">True if <c>--force</c> was supplied.</param>
public sealed record InitContext(
    string ProjectPath,
    string? ProjectName,
    string? Language,
    bool Force) : WorkloadContext(ProjectPath);
