// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

internal sealed class ProcessEnvironment : IProcessEnvironment
{
    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Environment.GetEnvironmentVariable(name);
    }
}
