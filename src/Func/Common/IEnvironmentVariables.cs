// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Thin abstraction over process environment-variable reads. Exists so
/// product code never touches <see cref="System.Environment.GetEnvironmentVariable(string)"/>
/// directly and tests can substitute their own values without mutating
/// process-global state (which would leak across xUnit parallel test runs).
/// </summary>
internal interface IEnvironmentVariables
{
    /// <summary>
    /// Returns the value of the environment variable named <paramref name="name"/>,
    /// or <c>null</c> if it is not set.
    /// </summary>
    public string? Get(string name);
}
