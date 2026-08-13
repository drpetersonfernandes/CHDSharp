# Building

How to build, test, pack, and publish every component of the repository.

---

## Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later (the solution targets `net8.0`, `net9.0`, and `net10.0`; the SDK's `rollForward` is set to `latestMajor` with prereleases allowed).
- Works on Windows, Linux, and macOS.
- No native toolchains are required — the library is 100% managed.

---

## Solution layout

```
CSharp_CHDSharp.sln
├── CHDSharpLib/          The library (NuGet package: CHDSharp)
├── CHDSharpCli/          Command-line verification tool
├── CHDSharpEncoder/      Companion encoder support library
├── CHDSharpTest/         xUnit unit + corpus tests
├── CHDSharpTestGen/      Deterministic corpus generator
├── CHDSharpTester/       WPF interactive tester
├── References/           libchdr-0.3.0 (C) + mame-mame0288 sources + format notes
└── Directory.Build.props Centralized versioning (1.3.0) and analyzer setup
```

---

## Build

```bash
# Everything (Debug)
dotnet build

# Release
dotnet build -c Release

# A single project
dotnet build CHDSharpLib/CHDSharpLib.csproj -c Release
```

### Centralized versioning

`Directory.Build.props` defines a single `<Version>` (currently `1.3.0`) that all projects inherit, so `AssemblyVersion`, `FileVersion`, and the NuGet package version stay in sync automatically.

### Code style & analyzers

All projects enable:

- `Nullable` + `ImplicitUsings`.
- `LangVersion 14`.
- [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) (build-time analyzer, `PrivateAssets=all`).
- `CHDSharpLib` additionally treats missing XML documentation (`CS1591`) as an error, so **all public API members must carry XML doc comments**.

---

## Test

```bash
# Run the full suite (all target frameworks)
dotnet test

# A single framework
dotnet test -f net10.0

# A specific test class
dotnet test --filter "FullyQualifiedName~CorpusTests"

# Verbose console output
dotnet test --logger "console;verbosity=detailed"
```

The suite contains **468 tests** (unit + corpus) that run against 30 deterministic CHD fixtures covering V1–V5 and every codec. See [Testing](testing.md).

> **Regenerating the corpus** requires the vintage `chdman`/`hdcomp` binaries in `CHDSharpTest/chdman/`:
>
> ```bash
> dotnet run --project CHDSharpTestGen
> ```
>
> The generator is deterministic: regenerating produces byte-identical fixtures.

---

## Pack (NuGet)

```bash
dotnet pack CHDSharpLib/CHDSharpLib.csproj -c Release
```

The package (`CHDSharp.<version>.nupkg`) is written to `CHDSharpLib/bin/Release/`. It includes:

- The library DLL for `net8.0`, `net9.0`, and `net10.0`.
- The README (this wiki's sibling pages are referenced from it), the MIT license, and the package icon.
- Embedded PDBs (`DebugType=embedded`) and embedded SourceLink for debugging.
- Deterministic, reproducible builds (`Deterministic=true`, `ContinuousIntegrationBuild` on GitHub Actions).

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port/) | 0.8.8 | Pure C# Zstd decompression |
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/) | 10.0.10 (all TFMs) | Pluggable logging (optional) |
| Microsoft.SourceLink.GitHub | 10.0.301 | SourceLink (build-time, `PrivateAssets=all`) |

`CHDSharpLib` also declares `InternalsVisibleTo` for `CHDSharpTest` and `CHDSharpTestGen` so the test projects can exercise internal members directly.

---

## Publish the CLI

```bash
# Framework-dependent
dotnet publish CHDSharpCli/CHDSharpCli.csproj -c Release -r win-x64 --self-contained false

# Self-contained single-file
dotnet publish CHDSharpCli/CHDSharpCli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The repository ships ready-made zip bundles under `CHDSharpLib/bin/Release/` (e.g. `CHDSharpCli_win-x64_v1.2.0.zip`).

---

## CI

The repository is set up for GitHub Actions (`ContinuousIntegrationBuild=true`), producing deterministic builds and SourceLink-enabled symbols. The test suite is the gatekeeper: all 468 tests must pass on all three target frameworks before a release.
