// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Base context shared by every workload invocation. Captures the inputs
/// every workload needs regardless of which command is running. Subclasses
/// add command-specific inputs (see <see cref="InitContext"/>).
/// </summary>
/// <param name="WorkingDirectory">The working directory the command is operating from (resolved from <c>[path]</c> or the current working directory).</param>
public abstract record WorkloadContext(WorkingDirectory WorkingDirectory);
