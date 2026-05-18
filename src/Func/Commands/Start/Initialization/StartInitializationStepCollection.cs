// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Ordered initialization step list.
/// </summary>
internal sealed class StartInitializationStepCollection : IReadOnlyList<IStartInitializationStep>
{
    private readonly List<IStartInitializationStep> _steps = [];

    public IStartInitializationStep this[int index] => _steps[index];

    public int Count => _steps.Count;

    public void Add<TStep>()
        where TStep : IStartInitializationStep, new()
    {
        Add(new TStep());
    }

    public void Add(IStartInitializationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
    }

    public IEnumerator<IStartInitializationStep> GetEnumerator() => _steps.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
