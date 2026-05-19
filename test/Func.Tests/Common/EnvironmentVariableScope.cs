// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tests.Common;

/// <summary>
/// Sets an environment variable for the lifetime of the scope and restores
/// the previous value on dispose. Tests rely on this to mutate process-global
/// env vars (e.g. <c>FUNC_CLI_WORKLOADS_HOME</c>) without leaking state to
/// later tests in the same process.
/// </summary>
internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    public EnvironmentVariableScope(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _previous);
    }
}
