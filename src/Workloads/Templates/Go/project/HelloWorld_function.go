package main

import (
	"net/http"

	"github.com/azure/azure-functions-golang-worker/sdk"
)

func init() {
	app.HTTP("HelloWorld", handleHelloWorld,
		sdk.WithMethods(http.MethodGet, http.MethodPost),
		sdk.WithAuth("anonymous"),
	)
}

func handleHelloWorld(w http.ResponseWriter, r *http.Request) {
	_, _ = w.Write([]byte("Hello from Go!"))
}
