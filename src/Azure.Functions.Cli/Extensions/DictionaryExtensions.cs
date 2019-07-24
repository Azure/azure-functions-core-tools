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

        public static bool SafeLeftMerge(this IDictionary<string, string> dictionary, IDictionary<string, string> another)
        {
            bool updated = false;
            foreach (var keyValPair in another)
            {
                if (!dictionary.ContainsKey(keyValPair.Key))
                {
                    dictionary.Add(keyValPair.Key, keyValPair.Value);
                    updated = true;
                }
            }
            return updated;
        }

        public static bool RemoveIfKeyValPresent(this IDictionary<string, string> dictionary, IDictionary<string, string> another)
        {
            bool removed = false;
            foreach (var anotherKeyValPair in another)
            {
                if (dictionary.TryGetValue(anotherKeyValPair.Key, out string myVal))
                {
                    if (myVal.Equals(anotherKeyValPair.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        dictionary.Remove(anotherKeyValPair.Key);
                        removed = true;
                    }
                }
            }
            return removed;
        }
    }
}
