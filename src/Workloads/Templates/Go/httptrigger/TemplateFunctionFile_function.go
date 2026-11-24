package main

import (
	"net/http"

	"github.com/azure/azure-functions-golang-worker/sdk"
)

func init() {
	app.HTTP("TemplateFunctionName", handleTemplateFunctionName,
		sdk.WithMethods(http.MethodGet, http.MethodPost),
		sdk.WithAuth("function"),
	)
}

func handleTemplateFunctionName(w http.ResponseWriter, r *http.Request) {
	_, _ = w.Write([]byte("Hello from Go!"))
}
