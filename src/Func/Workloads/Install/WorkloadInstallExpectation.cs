// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Carries the advisory inventory used for CLI interaction so preparation cannot silently expand stale consent.
/// </summary>
internal sealed record WorkloadInstallExpectation(IReadOnlyList<WorkloadEntry> Installed);
