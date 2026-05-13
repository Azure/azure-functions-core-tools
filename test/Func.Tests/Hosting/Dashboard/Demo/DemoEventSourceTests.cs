// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard.Demo;
using Azure.Functions.Cli.Hosting.Events;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Demo;

public class DemoEventSourceTests
{
    [Fact]
    public async Task ReadAsync_DefaultFunctionCount_DiscoversBaselineFiveFunctions()
    {
        // No burst, auto-exit on so the stream terminates after the scripted
        // opener and expansion. We expect exactly the 5 baseline functions
        // (HttpTrigger1, QueueProcessor, TimerCleanup, HttpTriggerOrders,
        // BlobIngest) to surface, regardless of how many discovery events
        // the timeline re-emits.
        var source = new DemoEventSource
        {
            SpeedMultiplier = 0.0001,
            AutoExit = true,
            BurstInvocationCount = 0,
        };

        HashSet<string> distinctNames = await CollectDistinctDiscoveredNames(source);

        Assert.Equal(5, distinctNames.Count);
        Assert.Contains("HttpTrigger1", distinctNames);
        Assert.Contains("QueueProcessor", distinctNames);
        Assert.Contains("TimerCleanup", distinctNames);
        Assert.Contains("HttpTriggerOrders", distinctNames);
        Assert.Contains("BlobIngest", distinctNames);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(20)]
    public async Task ReadAsync_FunctionCount_DiscoversRequestedNumberOfFunctions(int functionCount)
    {
        var source = new DemoEventSource
        {
            SpeedMultiplier = 0.0001,
            AutoExit = true,
            BurstInvocationCount = 0,
            FunctionCount = functionCount,
        };

        HashSet<string> distinctNames = await CollectDistinctDiscoveredNames(source);

        Assert.Equal(functionCount, distinctNames.Count);
        // Sequential extras start at HttpExtra1.
        for (int i = 1; i <= functionCount - 5; i++)
        {
            Assert.Contains($"HttpExtra{i}", distinctNames);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task ReadAsync_FunctionCountBelowBaseline_ClampsToFive(int functionCount)
    {
        var source = new DemoEventSource
        {
            SpeedMultiplier = 0.0001,
            AutoExit = true,
            BurstInvocationCount = 0,
            FunctionCount = functionCount,
        };

        HashSet<string> distinctNames = await CollectDistinctDiscoveredNames(source);

        Assert.Equal(5, distinctNames.Count);
    }

    private static async Task<HashSet<string>> CollectDistinctDiscoveredNames(DemoEventSource source)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var names = new HashSet<string>(StringComparer.Ordinal);

        await foreach (HostLogEntry entry in source.ReadAsync(cts.Token))
        {
            if (entry.Attributes.TryGetValue(HostLogAttributeKeys.CliEventKind, out object? kind)
                && kind is string s
                && s == CliEventKinds.FunctionDiscovered
                && entry.Attributes.TryGetValue(HostLogAttributeKeys.FunctionName, out object? name)
                && name is string n)
            {
                names.Add(n);
            }
        }

        return names;
    }
}
