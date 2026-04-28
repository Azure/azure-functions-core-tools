// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Base context shared by every workload invocation. Captures the inputs
/// every workload needs regardless of which command is running. Subclasses
/// add command-specific inputs (see <see cref="InitContext"/>).
/// </summary>
/// <param name="ProjectPath">The project directory the command is operating on (resolved from the path argument or the current working directory).</param>
public abstract record WorkloadContext(string ProjectPath);
