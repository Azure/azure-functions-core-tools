package main

import (
	"log"
	"net/http"

	"github.com/azure/azure-functions-golang-worker/sdk"
	"github.com/azure/azure-functions-golang-worker/worker"
)

// HTTPTriggerHandler handles standard HTTP requests
func HTTPTriggerHandler(w http.ResponseWriter, r *http.Request) {
	log.Printf("Processing HTTP Trigger for %s", r.URL.Path)
	w.WriteHeader(http.StatusOK)
	w.Write([]byte("Hello from Go Worker!"))
}

func main() {
	app := sdk.FunctionApp()
	app.HTTP("hello", HTTPTriggerHandler,
		sdk.WithMethods("GET", "POST"),
		sdk.WithAuth("anonymous"),
	)
	worker.Start(app)
}
