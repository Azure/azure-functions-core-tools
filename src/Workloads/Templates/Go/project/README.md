# FunctionsProject

This Azure Functions project requires Go 1.24 or later. Run `func start` to
build and run it. Run `go mod tidy` after changing its imports or dependencies.
Timer functions need Azurite or an Azure Storage connection configured in
`local.settings.json`.

Function files register their handlers with the shared `app` in `main.go`
during package initialization. Use a unique name for each function.
