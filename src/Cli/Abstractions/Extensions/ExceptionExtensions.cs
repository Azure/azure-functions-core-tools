// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Abstractions
{
    public static class ExceptionExtensions
    {
        internal const string CLIUserDisplayedException = "CLI_User_Displayed_Exception";

        public static TException DisplayAsError<TException>(this TException exception)
            where TException : Exception
            {
                exception.Data.Add(CLIUserDisplayedException, true);
                return exception;
            }

        public static void ReportAsWarning(this Exception e)
        {
            Reporter.Verbose.WriteLine($"Warning: Ignoring exception: {e.ToString().Yellow()}");
        }

        public static bool ShouldBeDisplayedAsError(this Exception e) => e.Data.Contains(CLIUserDisplayedException);
    }
}
