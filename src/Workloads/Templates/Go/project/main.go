package main

import (
	"github.com/azure/azure-functions-golang-worker/sdk"
	"github.com/azure/azure-functions-golang-worker/worker"
)

var app = sdk.FunctionApp()

func main() {
	worker.Start(app)
}
