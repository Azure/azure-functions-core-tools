// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Abstractions
{
    public class CommandParsingException : Exception
    {
        public CommandParsingException(
            string message,
            ParseResult? parseResult = null)
            : base(message)
        {
            ParseResult = parseResult!;
            Data.Add("CLI_User_Displayed_Exception", true);
        }

        public ParseResult ParseResult { get; set; }
    }
}
