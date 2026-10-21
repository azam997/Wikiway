namespace Wikiway.GameData;

// A sheet-wide index built on first use and then kept for the plugin's
// lifetime. Unlike Lazy<T>, a build that throws caches nothing, so the next
// caller retries instead of rethrowing the same exception forever.
internal sealed class LazyLookup<T>(Func<T> build) where T : class
{
    private readonly Lock gate = new();
    private T? value;

    public T Value
    {
        get
        {
            var current = value;
            if (current != null)
                return current;

            lock (gate)
            {
                return value ??= build();
            }
        }
    }
}
