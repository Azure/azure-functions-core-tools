// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <inheritdoc cref="IAzuriteHostEnvironment" />
internal sealed class AzuriteHostEnvironment : IAzuriteHostEnvironment
{
    public bool ExecutableExists(string candidatePath) => File.Exists(candidatePath);

    public string? GetPathVariable() => Environment.GetEnvironmentVariable("PATH");
}
