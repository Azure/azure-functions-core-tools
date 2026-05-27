// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Reads environment variables from the current CLI process. A seam over
/// <see cref="System.Environment.GetEnvironmentVariable(string)"/> so callers stay testable.
/// </summary>
internal interface IProcessEnvironment
{
    /// <summary>Returns the value of <paramref name="name"/>, or <c>null</c> when unset.</summary>
    public string? Get(string name);
}
