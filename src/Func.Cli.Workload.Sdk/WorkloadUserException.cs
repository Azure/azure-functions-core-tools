// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workload.Sdk;

/// <summary>
/// Thrown by a workload handler when the request is invalid for user-facing
/// reasons (bad input, missing dependency, etc.). The host surfaces the
/// message verbatim and exits with a user-error code.
/// </summary>
public sealed class WorkloadUserException(string message) : Exception(message);
