using System;
using System.Collections.Generic;
using System.Linq;

namespace Azure.Functions.Cli.Extensions
{
    public static class DictionaryExtensions
    {
        public static bool ContainsKeyCaseInsensitive(this IDictionary<string, string> dictionary, string key)
        {
            return dictionary.Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetValueCaseInsensitive(this IDictionary<string, string> dictionary, string key)
        {
            return dictionary.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
        }
    }
}
