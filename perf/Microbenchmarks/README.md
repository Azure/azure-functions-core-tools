# Implementation microbenchmarks

This project uses BenchmarkDotNet directly, without Crank or a child `func`
process. It references the CLI implementation and has access to its internal
types. It follows the repository's normal build, signing, and package conventions.
No benchmark cases are included yet.

## Run locally

Run from the repository root using the SDK selected by `global.json`:

```powershell
# List available benchmarks (empty until cases are added):
dotnet run --project ./perf/Microbenchmarks/Performance.Microbenchmarks.csproj -c Release -- --list flat

# Run selected benchmarks after adding cases:
dotnet run --project ./perf/Microbenchmarks/Performance.Microbenchmarks.csproj -c Release -- --filter '*MyBenchmarks*'
```

BenchmarkDotNet writes reports under `BenchmarkDotNet.Artifacts` in the current
working directory. These benchmarks are not scheduled in CI yet.

## Add a benchmark

Add a public class with public `[Benchmark]` methods in this project. The entry
point discovers benchmark classes automatically. Use `[GlobalSetup]` for
unmeasured setup and `[GlobalCleanup]` for cleanup; keep process startup and
other end-to-end measurements in `perf/EndToEnd` instead.
