# Logging

CHDSharp is **silent by default**. It integrates with `Microsoft.Extensions.Logging` so you can route internal diagnostics to any compatible provider.

---

## Enabling logging

Set the static `Chd.LoggerFactory` **before** performing library operations:

```csharp
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

var serilogLogger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

Chd.LoggerFactory = new SerilogLoggerFactory(serilogLogger);

// All subsequent Chd/ChdFile operations log through Serilog
var result = Chd.CheckFile(File.OpenRead("game.chd"), "game.chd", deepCheck: true);
```

Any `ILoggerFactory`-compatible provider works:

- [Serilog](https://serilog.net/) (`Serilog.Extensions.Logging`)
- [NLog](https://nlog-project.org/) (`NLog.Extensions.Logging`)
- `Microsoft.Extensions.Logging.Console`
- your own `ILoggerFactory` implementation

---

## What gets logged

| Area | Level | Examples |
|------|-------|----------|
| Verification | Information / Debug | progress percentages, array-pool statistics, compression-type statistics per CHD |
| Metadata | Debug | tag + length + ASCII payload of every metadata entry |
| Errors | Warning / Error | failed metadata reads, precache failures, decompression exceptions (with the inner exception and hunk number) |
| Per-codec | Debug | block summaries, repeated-block counts |

Because every log call is a pre-compiled `LoggerMessage.Define` delegate, the overhead is negligible when logging is disabled.

---

## Reset

To disable logging again (e.g. in tests):

```csharp
Chd.LoggerFactory = null;
```

---

## Example: log to a file with Serilog

```csharp
Chd.LoggerFactory = new SerilogLoggerFactory(
    new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.File("chdsharp.log", rollingInterval: RollingInterval.Day)
        .CreateLogger());
```

---

## Notes

- `LoggerFactory` is read lazily per operation, so you can swap providers at runtime; for predictable behavior, set it once at startup.
- The logging package (`Microsoft.Extensions.Logging.Abstractions`) is the library's only non-Zstd dependency and is marked optional in the sense that the library functions perfectly with it never set.
