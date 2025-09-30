// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions;

namespace Azure.Functions.Cli
{
    internal class CommandUnknownException : GracefulException
    {
        public CommandUnknownException(string commandName)
            : base(
            "Could not execute because the specified command or file was not found.")
        {
            InstructionMessage = string.Format(
                "Possible reasons for this include:"
                + "* You misspelled a built-in dotnet command."
                + "* You intended to execute a .NET program, but {0} does not exist."
                + "* You intended to run a global tool, but a dotnet-prefixed executable with this name could not be found on the PATH.",
                commandName);
        }

        public CommandUnknownException(string commandName, Exception innerException)
            : base(
            "Could not execute because the specified command or file was not found.")
        {
            InstructionMessage = string.Format(
                "Possible reasons for this include:"
                + "* You misspelled a built-in dotnet command."
                + "* You intended to execute a .NET program, but {0} does not exist."
                + "* You intended to run a global tool, but a dotnet-prefixed executable with this name could not be found on the PATH.",
                commandName);
        }

        public string InstructionMessage { get; } = string.Empty;
    }
}
