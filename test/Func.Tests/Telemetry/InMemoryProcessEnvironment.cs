// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.Telemetry;

internal sealed class InMemoryProcessEnvironment : IProcessEnvironment
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public void Set(string name, string? value)
    {
        if (value is null)
        {
            _values.Remove(name);
        }
        else
        {
            _values[name] = value;
        }
    }
}
