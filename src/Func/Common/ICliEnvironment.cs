// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Abstracts current-process properties so callers can be unit-tested without
/// a real host process.
/// </summary>
internal interface ICliEnvironment
{
    /// <summary>
    /// Gets the full path of the running executable, or <c>null</c> when the
    /// platform cannot determine it.
    /// </summary>
    public string? ProcessPath { get; }
}
