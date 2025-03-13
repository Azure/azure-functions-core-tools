using System.Collections.Generic;
using System.Linq;

namespace Azure.Functions.Cli.Helpers
{
    public static class EnumerationHelper
    {
        public static string Join<T>(string separator, IEnumerable<T> enumerable)
        {
             return enumerable.Select(t => t.ToString())
                        .Aggregate((total, next) => total + separator + next);
        }
    }
}