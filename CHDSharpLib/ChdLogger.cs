using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CHDSharp;

internal static class ChdLogger
{
    private static ILoggerFactory? _factory;

    public static ILoggerFactory? Factory
    {
        get => _factory;
        set
        {
            _factory = value;
            CachedLoggers.Clear();
        }
    }

    private static readonly Dictionary<string, ILogger> CachedLoggers = [];

    public static ILogger GetLogger(string category)
    {
        if (_factory is null)
            return NullLogger.Instance;

        if (!CachedLoggers.TryGetValue(category, out var logger))
        {
            logger = _factory.CreateLogger(category);
            CachedLoggers[category] = logger;
        }

        return logger;
    }

    public static ILogger GetLogger<T>()
    {
        return GetLogger(typeof(T).FullName!);
    }

    public static void Reset()
    {
        CachedLoggers.Clear();
    }
}
