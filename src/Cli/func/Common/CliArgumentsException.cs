// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Fclp;

namespace Azure.Functions.Cli.Common
{
    public class CliArgumentsException(string message, params CliArgument[] arguments) : Exception(message)
    {
        public CliArgumentsException(string message, ICommandLineParserResult parseResults, params CliArgument[] arguments)
            : this(message, arguments) => ParseResults = parseResults;

        public ICommandLineParserResult ParseResults { get; private set; }

        public IEnumerable<CliArgument> Arguments { get; private set; } = arguments;
    }

    public class CliArgument
    {
        public string Name { get; set; }

        public string Description { get; set; }
    }
}
