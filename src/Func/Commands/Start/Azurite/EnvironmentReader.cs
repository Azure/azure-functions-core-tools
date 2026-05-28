// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <inheritdoc cref="IEnvironmentReader" />
internal sealed class EnvironmentReader : IEnvironmentReader
{
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
