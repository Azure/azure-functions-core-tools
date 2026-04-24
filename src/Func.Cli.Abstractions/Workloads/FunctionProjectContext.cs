// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Describes the existing Functions project that a workload is operating on.
/// Captures information that lives on disk (not on the command line) so
/// providers don't each re-implement project discovery.
///
/// This will grow as we add more workload extension points (pack, deploy,
/// run, etc.) — anything that needs project-on-disk state belongs here.
/// </summary>
/// <param name="ProjectPath">The project directory.</param>
/// <param name="WorkerRuntime">The worker runtime the project was initialized for (e.g. "dotnet").</param>
/// <param name="Language">Programming language of the project (e.g. "C#", "TypeScript"). May be null when the runtime has a single language or it cannot be determined.</param>
public record FunctionProjectContext(
    string ProjectPath,
    string WorkerRuntime,
    string? Language);
