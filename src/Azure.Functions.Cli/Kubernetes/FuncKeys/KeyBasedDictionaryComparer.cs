using System.Collections.Generic;

namespace Azure.Functions.Cli.Kubernetes.FuncKeys
{
    public class KeyBasedDictionaryComparer : IEqualityComparer<KeyValuePair<string, string>>
    {
        public bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y)
        {
            //If the compared objects reference the same data.
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            //If any of the compared objects is null.
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
            {
                return false;
            }

            return x.Key == y.Key;
        }

        public int GetHashCode(KeyValuePair<string, string> keyValPair)
        {
            //If the keyValPair is null
            if (ReferenceEquals(keyValPair, null)) return 0;

            //Get hash code for the Key field.
            int hashKey = keyValPair.Key == null ? 0 : keyValPair.Key.GetHashCode();

            return hashKey;
        }
    }
}
