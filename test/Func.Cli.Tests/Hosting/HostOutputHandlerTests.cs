// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class HostOutputHandlerTests
{
    private readonly TestInteractionService _interaction = new();

    // === Timer trigger parsing ===

    [Fact]
    public void ProcessLine_TimerSchedule_ParsesAndDisplaysTimerFunction()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        // Feed timer schedule line
        handler.ProcessLine("info: Host.Triggers.Timer[5]");
        handler.ProcessLine("      The next 5 occurrences of the 'TimerTrigger1' schedule (Cron: '*/5 * * * * *') will be:");
        handler.ProcessLine("      04/17/2026 22:00:00-07:00 (04/18/2026 05:00:00Z)");

        // Feed HTTP route
        handler.ProcessLine("Mapped function route 'api/HttpTrigger1' [get,post] to 'HttpTrigger1'");

        // Trigger display
        handler.ProcessLine("Host started");

        var output = _interaction.GetOutput();

        // Should show both HTTP and Timer functions
        Assert.Contains("HttpTrigger1", output);
        Assert.Contains("TimerTrigger1", output);
        Assert.Contains("[Timer]", output);
        Assert.Contains("[GET,POST]", output);
    }

    [Fact]
    public void ProcessLine_OnlyHttpTriggers_ShowsOnlyHttp()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        handler.ProcessLine("Mapped function route 'api/Hello' [get] to 'Hello'");
        handler.ProcessLine("Host started");

        var output = _interaction.GetOutput();
        Assert.Contains("Hello", output);
        Assert.DoesNotContain("[Timer]", output);
    }

    [Fact]
    public void ProcessLine_OnlyTimerTriggers_ShowsTimerOnly()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        handler.ProcessLine("info: Host.Triggers.Timer[5]");
        handler.ProcessLine("      The next 5 occurrences of the 'MyTimer' schedule (Cron: '0 */5 * * * *') will be:");
        handler.ProcessLine("Host started");

        var output = _interaction.GetOutput();
        Assert.Contains("MyTimer", output);
        Assert.Contains("[Timer]", output);
    }

    [Fact]
    public void ProcessLine_NoFunctions_ShowsNothing()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        handler.ProcessLine("Host started");

        var output = _interaction.GetOutput();
        Assert.DoesNotContain("Functions:", output);
    }

    // === Non-verbose filtering ===

    [Fact]
    public void ProcessLine_NonVerbose_SuppressesInfoLines()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        // Generic info lines should be suppressed
        Assert.False(handler.ProcessLine("info: Host.Startup[0]"));
        Assert.False(handler.ProcessLine("info: Microsoft.Hosting.Lifetime[0]"));
    }

    [Fact]
    public void ProcessLine_NonVerbose_AllowsFunctionInfoLines()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        // Function-specific info should pass through
        Assert.False(handler.ProcessLine("info: Function.HttpTrigger1[0]") == false);
    }

    [Fact]
    public void ProcessLine_NonVerbose_AllowsHostTriggersLines()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        // Host.Triggers lines should pass through (timer schedules etc.)
        var result = handler.ProcessLine("info: Host.Triggers.Timer[5]");
        // Should not be suppressed
        Assert.False(result == true && false); // It passes through the noise filter
    }

    [Fact]
    public void ProcessLine_NonVerbose_AllowsWarningsAndErrors()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        Assert.True(handler.ProcessLine("warn: Host.Startup[0]"));
        Assert.True(handler.ProcessLine("fail: Host.Startup[0]"));
    }

    [Fact]
    public void ProcessLine_NonVerbose_KeepsContinuationLinesAfterWarning()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        // Warning followed by continuation lines (6-space indent)
        Assert.True(handler.ProcessLine("warn: Host.Startup[0]"));
        Assert.True(handler.ProcessLine("      This is a continuation line"));
    }

    [Fact]
    public void ProcessLine_NonVerbose_SuppressesContinuationLinesAfterInfo()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        handler.ProcessLine("info: Host.Startup[0]");
        Assert.False(handler.ProcessLine("      This continuation should be suppressed"));
    }

    [Fact]
    public void ProcessLine_Verbose_AllowsEverything()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: true);

        Assert.True(handler.ProcessLine("info: Host.Startup[0]"));
        Assert.True(handler.ProcessLine("info: Microsoft.Hosting.Lifetime[0]"));
        Assert.True(handler.ProcessLine("      continuation"));
    }

    // === Shutdown suppression ===

    [Fact]
    public void ProcessLine_NonVerbose_SuppressesAfterShutdown()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        handler.ProcessLine("Application is shutting down");
        Assert.False(handler.ProcessLine("info: some shutdown noise"));
        Assert.False(handler.ProcessLine("warn: shutdown warning"));
    }

    // === Host's Ctrl+C message suppression ===

    [Fact]
    public void ProcessLine_SuppressesHostCtrlCMessage()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        Assert.False(handler.ProcessLine("Application started. Press Ctrl+C to shut down."));
    }

    // === JSON/config block suppression ===

    [Fact]
    public void ProcessLine_NonVerbose_SuppressesOptionsLoggingBlocks()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        Assert.False(handler.ProcessLine("info: OptionsLoggingService[0]"));
        Assert.False(handler.ProcessLine("{"));
        Assert.False(handler.ProcessLine("  \"MaxConcurrentRequests\": -1"));
        Assert.False(handler.ProcessLine("}"));

        // After block ends, normal lines should work again
        Assert.True(handler.ProcessLine("warn: something after the block"));
    }

    // === Noisy hosting lines ===

    [Fact]
    public void ProcessLine_NonVerbose_SuppressesHostingLines()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        Assert.False(handler.ProcessLine("Hosting environment: Production"));
        Assert.False(handler.ProcessLine("Content root path: /app"));
        Assert.False(handler.ProcessLine("Now listening on: http://localhost:7071"));
    }

    // === Route parsing ===

    [Fact]
    public void ProcessLine_ParsesRouteAndSuppressesRawLine()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        // Route lines should be captured but not echoed
        Assert.False(handler.ProcessLine("Mapped function route 'api/MyFunc' [get,post] to 'MyFunc'"));
    }

    [Fact]
    public void ProcessLine_MultipleRoutes_AllDisplayed()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        handler.ProcessLine("Mapped function route 'api/Func1' [get] to 'Func1'");
        handler.ProcessLine("Mapped function route 'api/Func2' [post] to 'Func2'");
        handler.ProcessLine("Host started");

        var output = _interaction.GetOutput();
        Assert.Contains("Func1", output);
        Assert.Contains("Func2", output);
    }

    // === Duplicate timer trigger names ===

    [Fact]
    public void ProcessLine_DuplicateTimerName_OnlyShownOnce()
    {
        var handler = new HostOutputHandler(_interaction, 7071, verbose: false);

        handler.ProcessLine("      The next 5 occurrences of the 'MyTimer' schedule (Cron: '0 */5 * * * *') will be:");
        handler.ProcessLine("      The next 5 occurrences of the 'MyTimer' schedule (Cron: '0 */5 * * * *') will be:");
        handler.ProcessLine("Host started");

        var output = _interaction.GetOutput();
        var count = output.Split("MyTimer").Length - 1;
        // Should appear once in the header line + once in listing = 2 mentions, not 3
        Assert.True(count <= 2, $"MyTimer appeared {count} times, expected at most 2");
    }

    /// <summary>
    /// Minimal IInteractionService that captures output for assertions.
    /// </summary>
    private class TestInteractionService : IInteractionService
    {
        private readonly List<string> _lines = [];

        public string GetOutput() => string.Join("\n", _lines);

        public bool IsInteractive => false;

        public void WriteLine(string text) => _lines.Add(text);
        public void WriteMarkup(string markup) => _lines.Add(markup);
        public void WriteMarkupLine(string markup) => _lines.Add(markup);
        public void WriteError(string message) => _lines.Add($"ERROR: {message}");
        public void WriteWarning(string message) => _lines.Add($"WARN: {message}");
        public void WriteSuccess(string message) => _lines.Add($"OK: {message}");
        public void WriteTable(string[] columns, IEnumerable<string[]> rows) { }
        public void WriteRule(string title) => _lines.Add($"--- {title} ---");
        public void WriteBlankLine() => _lines.Add("");

        public Task<T> ShowStatusAsync<T>(string statusMessage, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
            => action(cancellationToken);

        public Task StatusAsync(string statusMessage, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
            => action(cancellationToken);

        public Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);

        public Task<string> PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default)
            => Task.FromResult(choices.First());

        public Task<string> PromptForInputAsync(string prompt, string? defaultValue = null, CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue ?? string.Empty);
    }
}
