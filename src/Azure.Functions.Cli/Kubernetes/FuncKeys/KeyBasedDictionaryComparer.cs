using System.Collections.Generic;

namespace Azure.Functions.Cli.Kubernetes.FuncKeys
{
    public class KeyBasedDictionaryComparer : IEqualityComparer<KeyValuePair<string, string>>
    {
        public bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y)
        {
            return string.Equals(x.Key, y.Key) && string.Equals(x.Value, y.Value);
        }

        public int GetHashCode(KeyValuePair<string, string> keyValPair)
        {
            // If the keyValPair is the default value
            if (keyValPair.Key.Equals(default(string)) && keyValPair.Value.Equals(default(string))) return 0;

            //Get hash code for the Key field.
            int hashKey = keyValPair.Key == null ? 0 : keyValPair.Key.GetHashCode();

            return hashKey;
        }
    }
}
