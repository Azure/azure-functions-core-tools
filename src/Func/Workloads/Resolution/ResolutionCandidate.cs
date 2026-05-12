// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// A workload that claimed a directory in an ambiguous resolution, with the
/// detector's reason carried through. Lets callers render disambiguation
/// errors in their own UX (verbose vs quiet, structured telemetry, etc.)
/// without re-parsing the resolver's prose message.
/// </summary>
/// <param name="Workload">The claiming workload.</param>
/// <param name="Reason">The detector's reason for claiming, if it supplied one.</param>
internal sealed record ResolutionCandidate(WorkloadInfo Workload, string? Reason);
