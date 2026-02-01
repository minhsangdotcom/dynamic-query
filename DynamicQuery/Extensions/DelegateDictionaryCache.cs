namespace DynamicQuery.Extensions;

public class DelegateDictionaryCache
{
    private static readonly Dictionary<string, Delegate> cache = [];

    public static Func<T, TResult> GetOrAdd<T, TResult>(string key, Func<Func<T, TResult>> factory)
    {
        if (cache.TryGetValue(key, out Delegate? cached))
        {
            return (Func<T, TResult>)cached;
        }

        if (cached == null)
        {
            cached = factory();
            cache[key] = cached;
        }

        return (Func<T, TResult>)cached;
    }
}
