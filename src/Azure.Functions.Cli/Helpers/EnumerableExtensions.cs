using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    public static class EnumerableExtensions
    {
        public static async Task<IEnumerable<T>> WaitAllAndUnwrap<T>(this IEnumerable<Task<T>> source)
        {
            await Task.WhenAll(source);
            return source.Select(t => t.Result);
        }
    }
}
