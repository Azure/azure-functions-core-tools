// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Production <see cref="IEnvironmentVariables"/> backed by
/// <see cref="System.Environment.GetEnvironmentVariable(string)"/>.
/// </summary>
internal sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    /// <inheritdoc />
    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return System.Environment.GetEnvironmentVariable(name);
    }
}
