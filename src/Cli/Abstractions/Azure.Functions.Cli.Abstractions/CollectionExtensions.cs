
namespace Azure.Functions.Cli.Abstractions
{
    public static class CollectionsExtensions
    {
        public static IEnumerable<T> OrEmptyIfNull<T>(this IEnumerable<T> enumerable)
        {
            return enumerable == null
                ? Enumerable.Empty<T>()
                : enumerable;
        }
    }
}
