package main

import (
	"context"
	"log/slog"

	"github.com/azure/azure-functions-golang-worker/sdk"
	"github.com/azure/azure-functions-golang-worker/sdk/bindings"
)

func init() {
	app.Timer("TemplateFunctionName", handleTemplateFunctionName,
		sdk.WithSchedule("__TIMER_SCHEDULE__"),
	)
}

func handleTemplateFunctionName(ctx context.Context, timer bindings.TimerInfo) error {
	slog.InfoContext(ctx, "Timer function executed", "past_due", timer.IsPastDue)
	return nil
}
