// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// User-expressed stack selection. Populated by
/// <see cref="Microsoft.Extensions.Options.IConfigureOptions{TOptions}"/>
/// implementations layered in precedence order (last registered wins).
/// Consumers depend on <c>IOptions&lt;StackOptions&gt;</c>; an empty
/// <see cref="Stack"/> means no pin and the caller should fall through to
/// project-based detection.
/// </summary>
internal sealed class StackOptions
{
    public string? Stack { get; set; }
}
