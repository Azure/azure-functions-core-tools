// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common
{
    internal class FunctionNotFoundException : Exception
    {
        public FunctionNotFoundException(string message)
            : base(message)
        {
        }

        public FunctionNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
