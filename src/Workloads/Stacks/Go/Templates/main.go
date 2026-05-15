package main

import (
"github.com/azure/azure-functions-golang-worker/sdk"
"github.com/azure/azure-functions-golang-worker/worker"
)

func main() {
    app := sdk.FunctionApp()
    // Register your functions on `app` here, then start the worker.
    // See https://aka.ms/azure-functions/go for examples.
    worker.Start(app)
}
