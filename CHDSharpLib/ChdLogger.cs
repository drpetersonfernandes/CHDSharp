using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CHDSharp;

internal static class ChdLogger
{
    private static volatile ILoggerFactory? _factory;

    public static ILoggerFactory? Factory
    {
        get => _factory;
        set => _factory = value;
    }

    /// <summary>
    /// Returns a logger that resolves the real logger from <see cref="Factory"/>
    /// on every use. This makes it safe to capture the returned instance in
    /// <c>static readonly</c> fields before the factory has been assigned.
    /// </summary>
    public static ILogger GetLogger(string category)
    {
        return new LazyLogger(category);
    }

    public static ILogger GetLogger<T>()
    {
        return GetLogger(typeof(T).FullName!);
    }

    private sealed class LazyLogger(string category) : ILogger
    {
        private sealed record CachedLogger(ILoggerFactory SourceFactory, ILogger Logger);

        private volatile CachedLogger? _cached;
        private readonly string _category = category;

        private ILogger Resolve()
        {
            var factory = _factory;
            if (factory is null)
                return NullLogger.Instance;

            var cached = _cached;
            if (cached is null || !ReferenceEquals(cached.SourceFactory, factory))
            {
                cached = new CachedLogger(factory, factory.CreateLogger(_category));
                _cached = cached;
            }

            return cached.Logger;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return Resolve().BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return Resolve().IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Resolve().Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
