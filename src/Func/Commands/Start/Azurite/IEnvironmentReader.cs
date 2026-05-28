// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Substitutable view of process environment variables. Lets discovery code
/// inspect <c>PATH</c> (and similar) without coupling to
/// <see cref="System.Environment"/> statics.
/// </summary>
internal interface IEnvironmentReader
{
    public string? GetEnvironmentVariable(string name);
}
