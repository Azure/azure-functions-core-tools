﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/CommandLoggingContext.cs

using Azure.Functions.Cli.Abstractions.Environment;
using Azure.Functions.Cli.Abstractions.Logging;

namespace Azure.Functions.Cli.Abstractions.Command
{
    /// <summary>
    /// Defines settings for logging.
    /// </summary>
    public static class CommandLoggingContext
    {
        public static class Variables
        {
            private const string Prefix = "DOTNET_CLI_CONTEXT_";
            public static readonly string Verbose = Prefix + "VERBOSE";
            internal static readonly string Output = Prefix + "OUTPUT";
            internal static readonly string Error = Prefix + "ERROR";
            internal static readonly string AnsiPassThru = Prefix + "ANSI_PASS_THRU";
        }

        private static Lazy<bool> s_verbose = new(() => Env.GetEnvironmentVariableAsBool(Variables.Verbose));
        private static Lazy<bool> s_output = new(() => Env.GetEnvironmentVariableAsBool(Variables.Output, true));
        private static Lazy<bool> s_error = new(() => Env.GetEnvironmentVariableAsBool(Variables.Error, true));
        private static readonly Lazy<bool> s_ansiPassThru = new(() => Env.GetEnvironmentVariableAsBool(Variables.AnsiPassThru));

        /// <summary>
        /// True if the verbose output is enabled.
        /// </summary>
        public static bool IsVerbose => s_verbose.Value;

        public static bool ShouldPassAnsiCodesThrough => s_ansiPassThru.Value;

        /// <summary>
        /// Sets or resets the verbose output.
        /// </summary>
        /// <remarks>
        /// After calling, consider calling <see cref="Reporter.Reset()"/> to apply change to reporter.
        /// </remarks>
        public static void SetVerbose(bool value)
        {
            s_verbose = new Lazy<bool>(() => value);
        }

        /// <summary>
        /// Sets or resets the normal output.
        /// </summary>
        /// <remarks>
        /// After calling, consider calling <see cref="Reporter.Reset()"/> to apply change to reporter.
        /// </remarks>
        public static void SetOutput(bool value)
        {
            s_output = new Lazy<bool>(() => value);
        }

        /// <summary>
        /// Sets or resets the error output.
        /// </summary>
        /// <remarks>
        /// After calling, consider calling <see cref="Reporter.Reset()"/> to apply change to reporter.
        /// </remarks>
        public static void SetError(bool value)
        {
            s_error = new Lazy<bool>(() => value);
        }

        /// <summary>
        /// True if normal output is enabled.
        /// </summary>
        internal static bool OutputEnabled => s_output.Value;

        /// <summary>
        /// True if error output is enabled.
        /// </summary>
        internal static bool ErrorEnabled => s_error.Value;
    }

}
