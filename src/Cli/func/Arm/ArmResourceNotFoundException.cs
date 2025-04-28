// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Arm
{
    internal class ArmResourceNotFoundException : Exception
    {
        public ArmResourceNotFoundException(string message)
            : base(message)
        {
        }

        public ArmResourceNotFoundException(string message, Exception exception)
            : base(message, exception)
        {
        }
    }
}
