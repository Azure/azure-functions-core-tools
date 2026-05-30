# Azure.Functions.Cli.Workloads.Go

## 1.0.0-preview.1

- Initial scaffold of the Go workload (entry point + stub project initializer).
- Run `go build` before host start so the Go worker can launch the project binary.
- Validate the Go toolchain (Go 1.24+) with a clear install hint when missing or too old, and emit the binary as `bin/app` (`bin\app.exe` on Windows) to match the Go worker's `defaultExecutablePath`.
- Sync the worker SDK with `go get github.com/azure/azure-functions-golang-worker@v0.6.0-preview` + `go mod tidy` on `func start`, so apps scaffolded against an older preview pull the matching SDK before building.
- Scaffold `main.go` with an HTTP-triggered "hello" function instead of an empty `main`, so a fresh `func init` is runnable end to end.
