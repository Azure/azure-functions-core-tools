using System.Collections.Generic;
using System.Linq;

namespace Azure.Functions.Cli.Extensions
{
    internal static class IEnumerableExtensions
    {
        public static IEnumerable<T> NotDefaults<T>(this IEnumerable<T> collection)
        {
            return collection.Where(e => !EqualityComparer<T>.Default.Equals(e, default(T)));
        }
    }
}