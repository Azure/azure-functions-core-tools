// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Binds advisory reuse to the selected registry row and cached payload bytes without promoting their source trust.
/// </summary>
internal sealed record PreparedWorkloadReuse(WorkloadEntry Entry, IReadOnlyDictionary<string, string> Hashes);
