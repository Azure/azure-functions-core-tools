// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Common;

public class ConsoleReader : IConsoleReader
{
    public ConsoleKeyInfo ReadKey(bool intercept = true) => Console.ReadKey(intercept);
}
