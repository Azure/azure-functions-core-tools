// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common
{
    internal class CliException : Exception
    {
        public CliException(string message)
            : base(message)
        {
        }

        public CliException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
