using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Helpers
{
    static class EnvironmentHelper
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
    }
}
