# CHDSharpTester

**WPF desktop application for batch-testing CHD files using the CHDSharp library and MAME chdman.**

> Cross-checks CHDSharpLib's decompression against `chdman` output, with PDF report export.
> Also includes codec tests (cdzs/zstd) and parent/child chain tests — previously in CHDSharpTest.

---

## Features

- **Batch CHD testing** — Add individual files or scan entire folders recursively
- **Per-file test suite** — Each CHD receives a comprehensive battery of tests
- **chdman cross-validation** — Automatically detects `chdman.exe` in the application folder and runs parallel verification
- **Session-level tests** — Zstd/cdzs codec decode tests and parent/child chain tests (run after per-file tests)
- **Live progress** — Real-time progress bar, per-file status, current test name, and scrollable log
- **Results browser** — Expandable per-file results with individual sub-test status, timing, and details
- **PDF export** — Export full test session results to a styled PDF report
- **Clipboard support** — Copy log text or formatted results to clipboard

---

## Getting Started

### Prerequisites

- .NET 10.0 SDK or runtime
- `chdman.exe` (from MAME) placed next to the executable — automatically detected on startup

### Build & Run

```bash
dotnet build CHDSharpTester\CHDSharpTester.csproj -c Release
dotnet run --project CHDSharpTester
```

### Using the App

1. Browse for `chdman.exe` (auto-detected if present in the app folder)
2. Click **Add Files** or **Add Folder** to select CHD files to test
3. Click **Run All Tests**
4. Browse results in the expandable file list
5. Export to PDF or copy results via the buttons

---

## Project Structure

```
CHDSharpTester/
├── ViewModels/
│   └── MainViewModel.cs      — Primary view model (file selection, test orchestration, results)
├── Views/
│   ├── MainWindow.xaml/.cs   — WPF FluentWindow host
│   └── MainPage.xaml/.cs     — Main UI page (configuration, progress, results)
├── Models/
│   ├── ChdFileEntry.cs       — Selected CHD file entry
│   ├── TestConfiguration.cs  — Test run parameters
│   ├── TestSessionResult.cs  — Full session results aggregation
│   └── PerFileResult.cs      — Per-file test results with sub-tests
├── Services/
│   ├── ChdTestRunner.cs      — Orchestrates test execution per file + session-level tests
│   ├── ChdmanWrapper.cs      — Invokes chdman.exe (info, verify, extractraw, copy) and parses output
│   ├── HashUtil.cs           — SHA1 hex formatting
│   ├── PdfExporter.cs        — PDF report generation (QuestPDF)
│   └── TestProgress.cs       — Progress reporting model
├── App.xaml/.cs              — Application entry point, Serilog config
└── chdman.exe                — Bundled chdman (copied to output on build)
```

---

## UI Overview

### Configuration Panel
- **chdman.exe path** — Auto-filled if found, or browse to select
- **File list** — Add/remove CHD files, shows filename and size
- **Run All Tests** — Starts the test session

### Progress Panel (during test run)
- **Progress bar** — Overall session progress
- **Current test** — Name of the test currently executing
- **Log window** — Timestamped log output, auto-scrolling

### Results Panel (after test run)
- **Summary cards** — Passed (green), Failed (red), Skipped (orange) file counts
- **Per-file expander** — Each file's sub-tests with checkmark/cross icons, times, and details
- **Export** — PDF report or clipboard copy

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [WPF-UI](https://www.nuget.org/packages/WPF-UI/) | 4.3.0 | Modern WPF Fluent Design controls |
| [QuestPDF](https://www.nuget.org/packages/QuestPDF/) | 2026.7.1 | PDF report generation |
| [Serilog](https://www.nuget.org/packages/Serilog/) | 4.4.0 | File + debug logging |
| [Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/) | 7.0.0 | Log file sink |
| [Serilog.Sinks.Debug](https://www.nuget.org/packages/Serilog.Sinks.Debug/) | 3.0.0 | Debug output sink |
| `CHDSharpLib` | (project reference) | Core CHD library |

---

## Test Categories

### Per-file tests (run on each selected CHD)

| Test | Requires chdman | Description |
|------|:---:|-------------|
| Header Basic Check | No | Validates `MComprHD` magic bytes and format version (V1–V5) |
| Header vs chdman | Yes | Cross-checks every header field against `chdman info` output |
| Deep Verification | No | Full hunk decompression with per-hunk CRC validation |
| chdman Verify | Yes | Runs `chdman verify -i file` and checks exit code |
| Full SHA1 | No | Sequential full-image read, SHA1 comparison against header |
| Random Access | Yes | 7 byte ranges compared byte-for-byte against `chdman extractraw` |
| ReadHunk API Tests | No | ReadHunk bounds errors, undersized buffer, determinism, Read vs ReadHunk consistency, cross-hunk boundary read, Read beyond end, header property sanity |

### Session-level tests (run once after all per-file tests)

| Test | Requires chdman | Description |
|------|:---:|-------------|
| cdzs Codec | Yes | Recompress a CD-type CHD to cdzs, decode with C#, compare SHA1 + CheckFile |
| zstd Codec | Yes | Recompress a CHD to zstd, decode with C#, compare SHA1 + CheckFile |
| Parent/Child Chain | Yes | Build parent/child pair, test requires parent, wrong parent rejection, full read through chain, CheckFileWithParent, chdman verify with parent |

Tests requiring `chdman.exe` are automatically skipped if it is not configured.

---

For unit tests (header parsing, CRC checksums, error paths) that require no external files, see `CHDSharpTest`.
For regenerating the deterministic test corpus, see `CHDSharpTestGen`.
