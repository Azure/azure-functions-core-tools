// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.UnitTests.Helpers;

internal class TestConsoleReader : IConsoleReader
{
    private readonly Queue<ConsoleKeyInfo> _keys = new Queue<ConsoleKeyInfo>();

    public void SimulateKeyPress(ConsoleKeyInfo key)
    {
        _keys.Enqueue(key);
    }

    public ConsoleKeyInfo ReadKey(bool intercept = true)
    {
        while (_keys.Count == 0)
        {
            Thread.Sleep(10); // Prevent tight spin-loop, allow test to enqueue keys
        }

        return _keys.Dequeue();
    }
}
