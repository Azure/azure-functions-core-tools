// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Indicates that the requested workload inventory is unavailable, rather than empty.
/// </summary>
internal sealed class WorkloadCatalogException(string message, Exception? inner = null) : Exception(message, inner);
