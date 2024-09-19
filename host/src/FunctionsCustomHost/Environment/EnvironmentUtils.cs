// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace FunctionsNetHost
{
    internal static class EnvironmentUtils
    {
#if OS_LINUX
        [System.Runtime.InteropServices.DllImport("libc")]
        private static extern int setenv(string name, string value, int overwrite);
#endif

        /// <summary>
        /// Gets the environment variable value.
        /// </summary>
        internal static string? GetValue(string environmentVariableName)
        {
            return Environment.GetEnvironmentVariable(environmentVariableName);
        }

        /// <summary>
        /// Sets the environment variable value.
        /// </summary>
        internal static void SetValue(string name, string value)
        {
            /*
             *  Environment.SetEnvironmentVariable is not setting the value of the parent process in Unix.
             *  So using the native method directly here.
             * */
#if OS_LINUX
            setenv(name, value, 1);
#else
            Environment.SetEnvironmentVariable(name, value);
#endif
        }
    }
}
