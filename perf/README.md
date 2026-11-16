# Performance

Performance infrastructure has two categories:

| Category | Location | Purpose |
| --- | --- | --- |
| End-to-end | [EndToEnd](EndToEnd/README.md) | Crank measures published, runtime-specific, self-contained `func` commands, including process startup and shutdown. |
| Microbenchmarks | [Microbenchmarks](Microbenchmarks/README.md) | Regular BenchmarkDotNet measures individual implementation operations without launching the CLI. |

Both projects are included in `Azure.Functions.Cli.slnx`. CI currently runs only
the end-to-end benchmarks; the microbenchmark project is ready for cases to be
added and intentionally contains none.

Both projects inherit the root `Directory.Build.props`,
`Directory.Build.targets`, `Directory.Packages.props`, and `NuGet.Config`.
Build outputs go under the repository's `out/bin` and `out/obj` directories.
