// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Indicates that host workload resolution failed because of user-provided start options.
/// </summary>
internal sealed class HostWorkloadResolutionException(string message) : Exception(message);
