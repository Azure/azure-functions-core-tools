using System;
using System.Collections.Generic;
using Fclp;

namespace Azure.Functions.Cli.Common
{
    public class CliArgumentsException : Exception
    {
        public ICommandLineParserResult ParseResults { get; private set; }

        public IEnumerable<CliArgument> Arguments { get; private set; }

        public CliArgumentsException(string message, params CliArgument[] arguments) : base(message)
        {
            Arguments = arguments;
        }

        public CliArgumentsException(string message, ICommandLineParserResult parseResults, params CliArgument[] arguments): this (message, arguments)
        {
            ParseResults = parseResults;
        }
    }

    public class CliArgument
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
