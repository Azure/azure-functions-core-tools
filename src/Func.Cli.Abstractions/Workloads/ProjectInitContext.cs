// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Inputs to <see cref="IProjectInitializer.InitializeAsync"/>. Captures the
/// resolved CLI inputs that every initializer needs.
/// </summary>
/// <param name="ProjectPath">Target directory for the new project.</param>
/// <param name="ProjectName">Project name (from --name or directory name).</param>
/// <param name="Language">Programming language (from --language or prompt). May be null.</param>
/// <param name="Force">True if --force was supplied.</param>
public record ProjectInitContext(
    string ProjectPath,
    string? ProjectName,
    string? Language,
    bool Force);
