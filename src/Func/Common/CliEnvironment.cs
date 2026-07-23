// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <inheritdoc cref="ICliEnvironment" />
internal sealed class CliEnvironment : ICliEnvironment
{
    public string? ProcessPath => Environment.ProcessPath;
}
