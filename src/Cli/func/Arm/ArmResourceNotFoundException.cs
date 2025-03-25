using System;

namespace Azure.Functions.Cli.Arm
{
    internal class ArmResourceNotFoundException : Exception
    {
        public ArmResourceNotFoundException(string message) : base(message)
        { }

        public ArmResourceNotFoundException(string message, Exception exception) : base(message, exception)
        { }
    }
}
