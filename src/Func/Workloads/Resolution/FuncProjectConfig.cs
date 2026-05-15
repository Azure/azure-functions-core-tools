// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Parsed shape of <c>.func/config.json</c>. Both fields are optional;
/// callers treat null/whitespace as "not set" and fall through to the next
/// resolution step.
/// </summary>
/// <param name="Stack">Workload alias (matched the same way as <c>--stack</c>).</param>
/// <param name="Language">Programming language hint (e.g. "python", "typescript").</param>
internal sealed record FuncProjectConfig(string? Stack, string? Language);
