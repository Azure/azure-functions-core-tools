// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Interfaces;

public interface IConsoleReader
{
    public ConsoleKeyInfo ReadKey(bool intercept);
}
