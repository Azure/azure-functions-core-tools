// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Kubernetes.FuncKeys
{
    public class KeyBasedDictionaryComparer : IEqualityComparer<KeyValuePair<string, string>>
    {
        public bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y)
        {
            return string.Equals(x.Key, y.Key);
        }

        public int GetHashCode(KeyValuePair<string, string> keyValPair)
        {
            // Get hash code for the Key field.
            int hashKey = keyValPair.Key == null ? 0 : keyValPair.Key.GetHashCode();

            return hashKey;
        }
    }
}
