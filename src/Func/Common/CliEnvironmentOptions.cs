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
    /// Full path of the running executable. Defaults to
    /// <see cref="Environment.ProcessPath"/>.
    /// </summary>
    public string ProcessPath { get; set; } = Environment.ProcessPath ?? string.Empty;
}
