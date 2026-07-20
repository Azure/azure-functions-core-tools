// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Inputs to <see cref="IProjectInitializer.InitializeAsync"/>. Resolved
/// values for the built-in <c>func init</c> options.
/// </summary>
/// <param name="WorkingDirectory">The working directory the command is operating from.</param>
/// <param name="ProjectName">Project name (from <c>--name</c> or the directory name).</param>
/// <param name="Language">Programming language (from <c>--language</c> or a prompt). May be null.</param>
/// <param name="Force">True if <c>--force</c> was supplied.</param>
public sealed record InitContext(WorkingDirectory WorkingDirectory, string? ProjectName, string? Language, bool Force);
