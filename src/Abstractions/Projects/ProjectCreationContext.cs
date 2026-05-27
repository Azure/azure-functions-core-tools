// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Inputs passed to workload project factories.
/// </summary>
/// <param name="WorkingDirectory">The directory the command is operating on.</param>
public sealed record ProjectCreationContext(WorkingDirectory WorkingDirectory);
