// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Inputs passed to workload project factories.
/// </summary>
/// <param name="WorkingDirectory">The directory the command is operating on.</param>
/// <param name="WorkerResolver">The resolver used to locate required Functions workers.</param>
public sealed record ProjectCreationContext(WorkingDirectory WorkingDirectory, IFunctionsWorkerResolver WorkerResolver);
