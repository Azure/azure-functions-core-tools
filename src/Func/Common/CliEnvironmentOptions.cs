// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Runtime environment options for the CLI process. Bind at startup and
/// inject via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
/// </summary>
internal sealed class CliEnvironmentOptions
{
    /// <summary>
    /// Full path of the running executable, or <c>null</c> when the platform
    /// cannot determine it.
    /// </summary>
    public string? ProcessPath { get; set; }
}
