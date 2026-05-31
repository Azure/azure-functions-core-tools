// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard;

public class DashboardStateTests
{
    [Fact]
    public void Observe_HostStateAttribute_EmitsHostStateChangedEvent()
    {
        var state = new DashboardState();
        var entry = MakeEntry(
            "Host.Lifecycle",
            LogLevel.Information,
            "Host ready",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "ready",
                [HostLogAttributeKeys.HostStartupDurationMs] = 1241.0,
            });

        IReadOnlyList<DashboardEvent> events = state.Observe(entry);

        var ev = Assert.Single(events.OfType<HostStateChangedEvent>());
        Assert.Equal(HostLifecycleState.Starting, ev.From);
        Assert.Equal(HostLifecycleState.Ready, ev.To);
        Assert.Equal(1241.0, ev.DurationMs);
    }

    [Fact]
    public void Observe_FunctionDiscovered_PopulatesSnapshot()
    {
        var state = new DashboardState();
        var entry = MakeEntry(
            "Host.Indexer",
            LogLevel.Information,
            "Function loaded",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionTriggerType] = "http",
                [HostLogAttributeKeys.FunctionRoute] = "/api/hello",
                [HostLogAttributeKeys.FunctionHttpMethods] = new[] { "GET", "POST" },
            });

        IReadOnlyList<DashboardEvent> events = state.Observe(entry);

        Assert.Single(events.OfType<FunctionDiscoveredEvent>());
        DashboardSnapshot snap = state.Snapshot();
        var fn = Assert.Single(snap.Functions);
        Assert.Equal("HttpTrigger1", fn.Name);
        Assert.Equal("http", fn.TriggerType);
        Assert.Equal(["GET", "POST"], fn.HttpMethods);
    }

    [Fact]
    public void Observe_InvocationLifecycle_UpdatesCounters()
    {
        var state = new DashboardState();
        state.Observe(DiscoverHttp("HttpTrigger1"));

        state.Observe(MakeEntry(
            "Function.HttpTrigger1",
            LogLevel.Information,
            "Executing 'HttpTrigger1'",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationStarted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-1",
            }));

        Assert.Equal(FunctionStatus.Active, state.Snapshot().Functions[0].Status);
        Assert.Equal(1, state.Snapshot().ActiveInvocationCount);

        IReadOnlyList<DashboardEvent> events = state.Observe(MakeEntry(
            "Function.HttpTrigger1",
            LogLevel.Information,
            "Executed 'HttpTrigger1'",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationCompleted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-1",
                [HostLogAttributeKeys.FunctionResult] = "succeeded",
                [HostLogAttributeKeys.DurationMs] = 12.0,
            }));

        Assert.Single(events.OfType<InvocationCompletedEvent>());
        DashboardSnapshot snap = state.Snapshot();
        Assert.Equal(1, snap.TotalInvocations);
        Assert.Equal(1, snap.SucceededInvocations);
        Assert.Equal(0, snap.FailedInvocations);
        Assert.Equal(0, snap.ActiveInvocationCount);
        Assert.Equal(FunctionStatus.Ready, snap.Functions[0].Status);
    }

    [Fact]
    public void Observe_FailedInvocation_FlagsFunctionAsError()
    {
        var state = new DashboardState();
        state.Observe(DiscoverHttp("HttpTrigger1"));
        var exceptionDetails = new HostLogExceptionDetails(
            "Microsoft.Azure.WebJobs.Host.FunctionInvocationException",
            "Exception while executing function",
            "outer stack",
            new HostLogExceptionDetails("Worker.UserException", "inner boom", "inner stack", null));

        IReadOnlyList<DashboardEvent> events = state.Observe(MakeEntry(
            "Function.HttpTrigger1",
            LogLevel.Error,
            "Executed 'HttpTrigger1' (Failed)",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationCompleted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-2",
                [HostLogAttributeKeys.FunctionResult] = "failed",
                [HostLogAttributeKeys.DurationMs] = 38.0,
            },
            exception: new InvalidOperationException("placeholder"),
            exceptionDetails: exceptionDetails));

        var completed = Assert.Single(events.OfType<InvocationCompletedEvent>());
        Assert.Equal("Microsoft.Azure.WebJobs.Host.FunctionInvocationException", completed.ErrorType);
        Assert.Equal("Exception while executing function", completed.ErrorMessage);
        Assert.Equal("Worker.UserException", completed.Error?.InnerException?.Type);
        Assert.Equal("inner boom", completed.Error?.InnerException?.Message);

        DashboardSnapshot snap = state.Snapshot();
        Assert.Equal(FunctionStatus.Error, snap.Functions[0].Status);
        Assert.Equal(1, snap.Functions[0].TotalErrors);
        Assert.Equal(1, snap.FailedInvocations);
        const string ExpectedLastErrorMessage =
            "Microsoft.Azure.WebJobs.Host.FunctionInvocationException: Exception while executing function" +
            " ---> Worker.UserException: inner boom";
        Assert.Equal(ExpectedLastErrorMessage, snap.Functions[0].LastErrorMessage);
        Assert.True(snap.ErrorCount > 0);
    }

    [Fact]
    public void Observe_HostRecycling_ClearsFunctionsOnReady()
    {
        var state = new DashboardState();
        state.Observe(DiscoverHttp("HttpTrigger1"));
        state.Observe(MakeEntry(
            "Host.Lifecycle",
            LogLevel.Information,
            "Recycling",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "recycling",
            }));

        Assert.Single(state.Snapshot().Functions); // not yet cleared

        state.Observe(MakeEntry(
            "Host.Lifecycle",
            LogLevel.Information,
            "Host ready",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "ready",
            }));

        Assert.Empty(state.Snapshot().Functions);
    }

    [Fact]
    public void BuildSummary_ReportsCounters()
    {
        var state = new DashboardState();
        state.Observe(DiscoverHttp("HttpTrigger1"));
        state.Observe(MakeEntry(
            "Function.HttpTrigger1",
            LogLevel.Information,
            string.Empty,
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationCompleted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-x",
                [HostLogAttributeKeys.FunctionResult] = "succeeded",
                [HostLogAttributeKeys.DurationMs] = 5.0,
            }));

        SummaryEvent summary = state.BuildSummary("sigint", DateTimeOffset.UtcNow);

        Assert.Equal("sigint", summary.ExitReason);
        Assert.Equal(1, summary.FunctionCount);
        Assert.Equal(1, summary.TotalInvocations);
        Assert.Equal(1, summary.SucceededInvocations);
    }

    private static HostLogEntry DiscoverHttp(string name) => MakeEntry(
        "Host.Indexer",
        LogLevel.Information,
        "Loaded",
        new()
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
            [HostLogAttributeKeys.FunctionName] = name,
            [HostLogAttributeKeys.FunctionTriggerType] = "http",
        });

    private static HostLogEntry MakeEntry(
        string category,
        LogLevel level,
        string message,
        Dictionary<string, object?> attributes,
        Exception? exception = null,
        HostLogExceptionDetails? exceptionDetails = null)
    {
        var entry = new HostLogEntry(DateTimeOffset.UtcNow, category, level, default, message, exception, attributes);
        return exceptionDetails is not null ? entry with { ExceptionDetails = exceptionDetails } : entry;
    }
}
