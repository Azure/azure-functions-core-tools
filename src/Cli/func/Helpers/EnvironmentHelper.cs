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

        public static void SetEnvironmentVariableAsBoolIfNotExists(string keyName)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(keyName)))
            {
                Environment.SetEnvironmentVariable(keyName, "true");
            }
        }
    }
}
