// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Host-environment operations the Azurite discovery code needs to perform.
/// Scoped to the Azurite feature so the abstraction stays narrow and tests
/// can substitute it without reaching for broad filesystem or environment
/// seams.
/// </summary>
internal interface IAzuriteHostEnvironment
{
    /// <summary>
    /// Returns <c>true</c> when an Azurite executable candidate exists at the
    /// given absolute path.
    /// </summary>
    public bool ExecutableExists(string candidatePath);

    /// <summary>
    /// Returns the raw value of the <c>PATH</c> environment variable, or
    /// <c>null</c> when it is not set.
    /// </summary>
    public string? GetPathVariable();
}
