// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Identifies a rejected workload source separately from failures contacting a valid feed.
/// </summary>
internal sealed class InvalidWorkloadSourceException(string message, Exception? innerException = null) : ArgumentException(message, innerException);