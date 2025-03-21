using System;

namespace Azure.Functions.Cli.Common
{
    internal class CliException : Exception
    {
        public CliException(string message) : base(message)
        { }

        public CliException(string message, Exception innerException) : base(message, innerException)
        { }
    }
}
