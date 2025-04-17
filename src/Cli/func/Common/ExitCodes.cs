// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common
{
    internal static class ExitCodes
    {
        public const int Success = 0;
        public const int GeneralError = 1;
        public const int MustRunAsAdmin = 2;
        public const int ParseError = 3;
        public const int BuildNativeDepsRequired = 4;
    }
}
