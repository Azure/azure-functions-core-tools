using System;

namespace Azure.Functions.Cli.Common
{
    internal class FunctionNotFoundException : Exception
    {
        public FunctionNotFoundException(string message) : base(message)
        { }

        public FunctionNotFoundException(string message, Exception innerException) : base(message, innerException)
        { }
    }
}
