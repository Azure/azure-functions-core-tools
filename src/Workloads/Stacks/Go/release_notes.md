# Azure.Functions.Cli.Workloads.Go

## 1.0.0-preview.1

- Initial scaffold of the Go workload (entry point + stub project initializer).
- Run `go build` before host start so the Go worker can launch the project binary.
- Validate the Go toolchain (Go 1.24+) with a clear install hint when missing or too old, and emit the binary as `bin/app` (`bin\app.exe` on Windows) to match the Go worker's `defaultExecutablePath`.
- Run `go mod tidy` before `go build` on `func start` so the version-less scaffold (and any drifted `go.sum`) resolves to the latest Go worker SDK release.
- Scaffold `main.go` with an HTTP-triggered "hello" function instead of an empty `main`, so a fresh `func init` is runnable end to end.
