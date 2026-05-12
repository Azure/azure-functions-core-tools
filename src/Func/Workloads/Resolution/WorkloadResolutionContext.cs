// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Inputs to <see cref="IWorkloadResolver.ResolveAsync"/>. Captured as a
/// record so the call sites at init / new / pack / start stay terse and we
/// can extend the inputs (selectors beyond <c>--stack</c>, environment
/// overrides, etc.) without breaking callers.
/// </summary>
/// <param name="Directory">The directory the command is operating on. Used by the local-settings lookup and detector pre-filter.</param>
/// <param name="StackSelector">Value of <c>--stack</c> (or equivalent), if the user supplied one. <c>null</c> means no explicit selector.</param>
internal sealed record WorkloadResolutionContext(DirectoryInfo Directory, string? StackSelector);
