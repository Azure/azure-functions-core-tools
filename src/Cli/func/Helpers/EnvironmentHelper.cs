// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Helpers
{
    internal static class EnvironmentHelper
    {
        public static bool GetEnvironmentVariableAsBool(string keyName)
        {
            string val = Environment.GetEnvironmentVariable(keyName);

            if (string.IsNullOrEmpty(val))
            {
                return false;
            }

            return val.Equals("1") || val.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads an environment variable as a tri-state boolean.
        /// </summary>
        /// <returns>
        /// true if the value is "1" or "true" (case-insensitive); false if the value
        /// is "0" or "false"; null if unset or unrecognized.
        /// </returns>
        public static bool? GetEnvironmentVariableAsNullableBool(string keyName)
        {
            string val = Environment.GetEnvironmentVariable(keyName);

            if (string.IsNullOrEmpty(val))
            {
                return null;
            }

            if (val.Equals("1") || val.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (val.Equals("0") || val.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }

        public static void SetEnvironmentVariableAsBoolIfNotExists(string keyName)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(keyName)))
            {
                Environment.SetEnvironmentVariable(keyName, "true");
            }
        }
    }
}
