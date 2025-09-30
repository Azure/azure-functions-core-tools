// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli
{
    public interface ICommandParser
    {
        public Command GetCommand();
    }
}
