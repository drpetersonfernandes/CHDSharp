# CHDSharpTest

**xUnit unit test suite for CHDSharp — no external file dependencies required.**

---

## Test Structure

| Class | Type | Description |
|-------|------|-------------|
| `HeaderAndApiTests` | Unit | Header magic validation, version detection, error paths |
| `ChecksumTests` | Unit | CRC-32 and CRC-16 test vectors |

---

## Running

```bash
# Run all tests
dotnet test

# Run a specific test class
dotnet test --filter "FullyQualifiedName~HeaderAndApiTests"

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"
```

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [xunit.v3](https://www.nuget.org/packages/xunit.v3/) | 3.2.2 | Test framework |
| [xunit.runner.visualstudio](https://www.nuget.org/packages/xunit.runner.visualstudio/) | 3.1.5 | Visual Studio test runner |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/) | 18.8.1 | .NET test SDK |
| [coverlet.collector](https://www.nuget.org/packages/coverlet.collector/) | 10.0.1 | Code coverage collector |
| `CHDSharpLib` | (project reference) | Core CHD library |

---

For integration tests with real CHD files and chdman cross-checking, see `CHDSharpTester`.
