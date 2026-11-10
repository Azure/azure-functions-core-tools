// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Console;

public class SpectreInteractionServiceTests
{
    [Fact]
    public void WriteRawLine_LongText_WritesVerbatimToInjectedStdout()
    {
        using var stdout = new BufferedConsole(interactive: false, ansi: false, noColor: true);
        using var stderr = new BufferedConsole(interactive: false, ansi: false, noColor: true);
        using var wrapped = new BufferedConsole(interactive: false, ansi: false, noColor: true);
        stdout.Profile.Width = 20;
        wrapped.Profile.Width = 20;
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        string text = $"[red]{new string('x', 200)}[/]";

        service.WriteRawLine(text);

        stdout.Output.Should().Be(text + Environment.NewLine);
        stderr.Output.Should().BeEmpty();
        wrapped.WriteLine(text);
        wrapped.Output.Should().NotBe(stdout.Output);
        wrapped.Output.Replace(Environment.NewLine, string.Empty).Should().Be(text);
    }

    [Fact]
    public void WriteRawLine_NullText_ThrowsBeforeWriting()
    {
        using var stdout = new BufferedConsole(interactive: false, ansi: false);
        using var stderr = new BufferedConsole(interactive: false, ansi: false);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        Action act = () => service.WriteRawLine(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("text");
        stdout.Output.Should().BeEmpty();
        stderr.Output.Should().BeEmpty();
    }

    public static IEnumerable<object[]> Capabilities()
    {
        foreach (bool input in new[] { false, true })
        foreach (bool stdoutAnsi in new[] { false, true })
        foreach (bool stderrAnsi in new[] { false, true })
        foreach (bool noColor in new[] { false, true })
        {
            yield return [input, stdoutAnsi, stderrAnsi, noColor];
        }
    }

    [Theory]
    [MemberData(nameof(Capabilities))]
    public void IsInteractive_UsesInjectedConsolesForWholeCommand(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        service.IsInteractive.Should().Be(input && stdoutAnsi && stderrAnsi);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IsInteractive_InjectedInputProfilesDisagree_ReturnsFalse(bool stdoutInput, bool stderrInput)
    {
        using var stdout = new BufferedConsole(stdoutInput, true);
        using var stderr = new BufferedConsole(stderrInput, true);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        service.IsInteractive.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task ConfirmAsync_UsesStdoutInputWithoutRequiringAnsi(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(!input, stderrAnsi, noColor);
        stdout.Enqueue(ConsoleKey.N, 'n');
        stdout.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        bool result = await service.ConfirmAsync("Continue?", defaultValue: true, CancellationToken.None);

        result.Should().Be(!input);
        stdout.Reads.Should().Be(input ? 2 : 0);
        stderr.Output.Should().BeEmpty();
        stderr.Reads.Should().Be(0);
        if (input) stdout.Output.Should().Contain("Continue?");
        else stdout.Output.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task PromptForSelectionAsync_UsesStderrInputAndAnsi(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(!input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        string result = await service.PromptForSelectionAsync("Pick", ["first", "second"], CancellationToken.None);

        bool canSelect = input && stderrAnsi;
        result.Should().Be(canSelect ? "second" : "first");
        AssertStderrPrompt(stdout, stderr, canSelect, 2);
    }

    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task PromptForMultiSelectionAsync_StringChoices_UsesStderrInputAndAnsi(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(!input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Spacebar, ' ');
        stderr.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        var result = await service.PromptForMultiSelectionAsync("Pick", new[] { "first", "second" }, CancellationToken.None);

        bool canSelect = input && stderrAnsi;
        result.Should().Equal(canSelect ? ["second"] : []);
        AssertStderrPrompt(stdout, stderr, canSelect, 3);
    }

    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task PromptForMultiSelectionAsync_LabelledChoices_ReturnsValues(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(!input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        stderr.Enqueue(ConsoleKey.Enter);
        stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Spacebar, ' ');
        stderr.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        var result = await service.PromptForMultiSelectionAsync(
            "Pick", new[] { new MultiSelectionChoice("first", "First label"), new MultiSelectionChoice("second", "Second label") }, CancellationToken.None);

        bool canSelect = input && stderrAnsi;
        result.Should().Equal(canSelect ? ["second"] : []);
        AssertStderrPrompt(stdout, stderr, canSelect, 4);
        if (canSelect) stderr.Output.Should().Contain("Second label");
    }

    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task PromptForInputAsync_UsesStderrInputWithoutRequiringAnsi(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(!input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        stderr.Enqueue(ConsoleKey.X, 'x');
        stderr.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        string result = await service.PromptForInputAsync("Pick", "fallback", CancellationToken.None);

        result.Should().Be(input ? "x" : "fallback");
        AssertStderrPrompt(stdout, stderr, input, 2);
    }

    [Theory]
    [InlineData(false, null, "")]
    [InlineData(false, "default", "default")]
    [InlineData(true, null, "")]
    [InlineData(true, "default", "default")]
    public async Task PromptForInputAsync_EmptyInput_PreservesDefault(bool input, string? defaultValue, string expected)
    {
        using var stdout = new BufferedConsole(false, false);
        using var stderr = new BufferedConsole(input, true);
        stderr.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        (await service.PromptForInputAsync("Pick", defaultValue, CancellationToken.None)).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConfirmAsync_EmptyInputOrUnavailableInput_PreservesDefault(bool input, bool defaultValue)
    {
        using var stdout = new BufferedConsole(input, false);
        using var stderr = new BufferedConsole(false, false);
        stdout.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        (await service.ConfirmAsync("Continue?", defaultValue, CancellationToken.None)).Should().Be(defaultValue);
        stdout.Reads.Should().Be(input ? 1 : 0);
    }

    public static IEnumerable<object[]> ConfirmationDefaults()
    {
        foreach (object[] capabilities in Capabilities())
        foreach (bool defaultValue in new[] { false, true })
        foreach (bool whenInputUnavailable in new[] { false, true })
        {
            yield return [.. capabilities, defaultValue, whenInputUnavailable];
        }
    }

    [Theory]
    [MemberData(nameof(ConfirmationDefaults))]
    public async Task ConfirmAsync_SeparateFallback_DoesNotChangeEnterDefaultOrLegacyOverload(
        bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor, bool defaultValue, bool whenInputUnavailable)
    {
        using var stdout = new BufferedConsole(input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(!input, stderrAnsi, noColor);
        stdout.Enqueue(ConsoleKey.Enter);
        stdout.Enqueue(ConsoleKey.Enter);
        IInteractionService service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        bool result = await service.ConfirmAsync("Continue?", defaultValue, whenInputUnavailable, CancellationToken.None);
        bool legacyResult = await service.ConfirmAsync("Continue?", defaultValue, CancellationToken.None);

        result.Should().Be(input ? defaultValue : whenInputUnavailable);
        legacyResult.Should().Be(defaultValue);
        stdout.Reads.Should().Be(input ? 2 : 0);
        stderr.Reads.Should().Be(0);
        stderr.Output.Should().BeEmpty();
        if (input) stdout.Output.Should().Contain("Continue?");
        else stdout.Output.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmAsync_InputFailure_DoesNotReturnUnavailableFallback(bool whenInputUnavailable)
    {
        using var stdout = new BufferedConsole(true, false);
        using var stderr = new BufferedConsole(false, false);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);

        await FluentActions.Awaiting(() => service.ConfirmAsync("Continue?", false, whenInputUnavailable, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*scripted input*");

        stdout.Reads.Should().Be(1);
        stderr.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionPrompts_NullChoices_RejectArguments(bool interactive)
    {
        using var console = new BufferedConsole(interactive, interactive);
        var service = new SpectreInteractionService(new DefaultTheme(), console, console);

        await FluentActions.Awaiting(() => service.PromptForSelectionAsync("Pick", null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => service.PromptForMultiSelectionAsync("Pick", (IEnumerable<string>)null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => service.PromptForMultiSelectionAsync("Pick", (IEnumerable<MultiSelectionChoice>)null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        console.Reads.Should().Be(0);
    }

    [Fact]
    public async Task PromptForSelectionAsync_MultiplePages_SelectsBeyondFirstPage()
    {
        using var stdout = new BufferedConsole(false, false);
        using var stderr = new BufferedConsole(true, true);
        for (int i = 0; i < 12; i++) stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        string[] choices = [.. Enumerable.Range(0, 15).Select(i => $"choice-{i}")];

        (await service.PromptForSelectionAsync("Pick", choices, CancellationToken.None)).Should().Be("choice-12");
        stderr.Reads.Should().Be(13);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionPrompts_EmptyChoices_DoNotReadOrRender(bool interactive)
    {
        using var console = new BufferedConsole(interactive, interactive);
        var service = new SpectreInteractionService(new DefaultTheme(), console, console);

        (await service.PromptForSelectionAsync("Pick", [], CancellationToken.None)).Should().BeEmpty();
        (await service.PromptForMultiSelectionAsync("Pick", Array.Empty<string>(), CancellationToken.None)).Should().BeEmpty();
        (await service.PromptForMultiSelectionAsync("Pick", Array.Empty<MultiSelectionChoice>(), CancellationToken.None)).Should().BeEmpty();
        console.Reads.Should().Be(0);
        console.Output.Should().BeEmpty();
    }

    public static IEnumerable<object[]> PromptMethods()
    {
        return typeof(IInteractionService).GetMethods()
            .Where(method => method.Name.StartsWith("PromptFor", StringComparison.Ordinal) || method.Name == nameof(IInteractionService.ConfirmAsync))
            .SelectMany(method => new[] { false, true }.Select(interactive => new object[] { method, interactive }));
    }

    [Theory]
    [MemberData(nameof(PromptMethods))]
    public async Task Prompts_InputCancellation_PropagatesWhenTargetCanPrompt(MethodInfo method, bool noColor)
    {
        using var console = new BufferedConsole(true, true, noColor);
        console.Enqueue(ConsoleKey.C, '\u0003', control: true);
        var service = new SpectreInteractionService(new DefaultTheme(), console, console);
        object?[] arguments = [.. method.GetParameters().Select(parameter => parameter.ParameterType switch
        {
            Type t when t == typeof(string) => (object)"Pick",
            Type t when t == typeof(bool) => parameter.Name == "whenInputUnavailable",
            Type t when t == typeof(CancellationToken) => CancellationToken.None,
            Type t when t == typeof(IEnumerable<string>) => new[] { "first", "second" },
            Type t when t == typeof(IEnumerable<MultiSelectionChoice>) => new[] { new MultiSelectionChoice("first") },
            _ => throw new InvalidOperationException($"Uncovered prompt parameter: {parameter}"),
        })];

        Func<Task> act = async () => await (Task)method.Invoke(service, arguments)!;

        await act.Should().ThrowAsync<OperationCanceledException>();
        console.Reads.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(PromptMethods))]
    public async Task Prompts_CanceledToken_ThrowsBeforeReadingOrFallback(MethodInfo method, bool interactive)
    {
        using var console = new BufferedConsole(interactive, interactive);
        var service = new SpectreInteractionService(new DefaultTheme(), console, console);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        object?[] arguments = [.. method.GetParameters().Select(parameter => parameter.ParameterType switch
        {
            Type t when t == typeof(string) => (object)"Pick",
            Type t when t == typeof(bool) => parameter.Name == "whenInputUnavailable",
            Type t when t == typeof(CancellationToken) => cancellation.Token,
            Type t when t == typeof(IEnumerable<string>) => Array.Empty<string>(),
            Type t when t == typeof(IEnumerable<MultiSelectionChoice>) => Array.Empty<MultiSelectionChoice>(),
            _ => throw new InvalidOperationException($"Uncovered prompt parameter: {parameter}"),
        })];

        Func<Task> act = async () =>
        {
            try
            {
                await (Task)method.Invoke(service, arguments)!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is OperationCanceledException canceled)
            {
                throw canceled;
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        console.Reads.Should().Be(0);
        console.Output.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task StatusAndProgress_ExecuteOnceAndForwardCancellation(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        using var cancellation = new CancellationTokenSource();
        int calls = 0;

        int result = await service.ShowStatusAsync("Loading", token =>
        {
            token.Should().Be(cancellation.Token);
            calls++;
            return Task.FromResult(42);
        }, cancellation.Token);
        await service.StatusAsync("Saving", token =>
        {
            token.Should().Be(cancellation.Token);
            calls++;
            return Task.CompletedTask;
        }, cancellation.Token);
        int progressResult = await service.RunWithProgressAsync("Resolving", (progress, token) =>
        {
            token.Should().Be(cancellation.Token);
            calls++;
            progress.SetDescription("Downloading");
            progress.SetTotal(2);
            progress.Report(1);
            progress.Increment(1);
            return Task.FromResult(7);
        }, cancellation.Token);

        result.Should().Be(42);
        progressResult.Should().Be(7);
        calls.Should().Be(3);
        stdout.Output.Should().BeEmpty();
        stdout.Reads.Should().Be(0);
        stderr.Reads.Should().Be(0);
        if (!(input && stdoutAnsi && stderrAnsi))
        {
            stderr.Output.Should().Contain("Loading...").And.Contain("Saving...").And.Contain("Resolving...").And.Contain("Downloading...");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpectrePrompts_WithoutAnsi_OnlySelectionRequiresAnsi(bool multiSelection)
    {
        using var console = new BufferedConsole(true, false);
        Func<Task> act = multiSelection
            ? async () => await new MultiSelectionPrompt<string>().AddChoices("one").ShowAsync(console, CancellationToken.None)
            : async () => await new SelectionPrompt<string>().AddChoices("one").ShowAsync(console, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*ANSI escape sequences*");
        console.Reads.Should().Be(0);
    }

    [Fact]
    public async Task SpectreTextAndConfirmation_WithoutAnsi_CanReadInput()
    {
        using var console = new BufferedConsole(true, false);
        console.Enqueue(ConsoleKey.X, 'x');
        console.Enqueue(ConsoleKey.Enter);
        console.Enqueue(ConsoleKey.N, 'n');
        console.Enqueue(ConsoleKey.Enter);

        (await new TextPrompt<string>("Text").ShowAsync(console, CancellationToken.None)).Should().Be("x");
        (await new ConfirmationPrompt("Confirm").ShowAsync(console, CancellationToken.None)).Should().BeFalse();
        console.Reads.Should().Be(4);
    }

    private static void AssertStderrPrompt(BufferedConsole stdout, BufferedConsole stderr, bool prompted, int reads)
    {
        stdout.Output.Should().BeEmpty();
        stdout.Reads.Should().Be(0);
        stderr.Reads.Should().Be(prompted ? reads : 0);
        if (prompted) stderr.Output.Should().Contain("Pick");
        else stderr.Output.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, "\u2713")]
    [InlineData(false, "[x]")]
    public async Task Progress_KeepsTitleAndCollapsesDetailOnSuccess(bool unicode, string check)
    {
        var (service, stdout, stderr) = CreateService(interactive: true, unicode);

        int result = await service.RunWithProgressAsync("Installing [workload]", (progress, _) =>
        {
            progress.SetDescription("Downloading [package]");
            Assert.Contains("Installing [workload]", stderr.ToString());
            Assert.Contains(unicode ? "\u2514 Downloading [package]" : "` Downloading [package]", stderr.ToString());
            Assert.DoesNotContain("100%", stderr.ToString());
            Assert.DoesNotContain("\u2501", stderr.ToString());
            Assert.DoesNotContain("\u2500", stderr.ToString());
            stderr.GetStringBuilder().Clear();
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.EndsWith($"{check} Installing [workload]{Environment.NewLine}", stderr.ToString());
        Assert.Empty(stdout.ToString());
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task Progress_CompletionStaysOnSpinnerRow(bool unicode, bool writeCompletion, bool hasDetail)
    {
        var writer = new CaptureWriter();
        IAnsiConsole console = CreateConsole(writer, interactive: true, unicode);
        var service = new SpectreInteractionService(new DefaultTheme(), console, console);
        service.WriteLine("Before operation");
        int spinnerRow = -1;

        await service.RunWithProgressAsync("Installing workload", (progress, _) =>
        {
            if (hasDetail)
            {
                progress.SetDescription("Downloading");
            }

            spinnerRow = GetRenderedRow(writer.GetStringBuilder().ToString(), "Installing workload");
            Assert.Equal(1, spinnerRow);
            return Task.FromResult(0);
        }, writeCompletion: writeCompletion);

        if (!writeCompletion)
        {
            service.WriteSuccess("Installed workload");
        }

        Assert.Equal(spinnerRow, GetRenderedRow(
            writer.GetStringBuilder().ToString(), writeCompletion ? "Installing workload" : "Installed workload"));
        Assert.DoesNotContain("\x1b[0A", writer.GetStringBuilder().ToString());
    }

    [Fact]
    public async Task Progress_FailureStaysOnSpinnerRow()
    {
        var (service, _, stderr) = CreateService(interactive: true);
        int spinnerRow = -1;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunWithProgressAsync<int>("Installing workload", (progress, _) =>
            {
                progress.SetDescription("Extracting");
                spinnerRow = GetRenderedRow(stderr.GetStringBuilder().ToString(), "Installing workload");
                Assert.Equal(0, spinnerRow);
                throw new InvalidOperationException("Invalid package");
            }, writeCompletion: false));

        Assert.Equal(spinnerRow, GetRenderedRow(stderr.GetStringBuilder().ToString(), "Installing workload"));
    }

    [Fact]
    public async Task Progress_RefreshesWhileWaitingForOperation()
    {
        var (service, _, stderr) = CreateService(interactive: true);

        await service.RunWithProgressAsync("Downloading", async (_, _) =>
        {
            await Task.Delay(250);
            return 0;
        });

        // Include a refresh beyond the initial frame, final frame, and completion row.
        Assert.True(Regex.Matches(stderr.ToString(), "Downloading").Count >= 4);
    }

    [Theory]
    [InlineData(true, "\u00d7", "\u2514")]
    [InlineData(false, "[!]", "`")]
    public async Task Progress_FailureRetainsLatestDetailAndPropagates(bool unicode, string failure, string gutter)
    {
        var (service, _, stderr) = CreateService(interactive: true, unicode);
        var expected = new InvalidOperationException("Download failed");

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunWithProgressAsync<int>("Installing workload", (progress, _) =>
            {
                progress.SetDescription("Resolving");
                progress.SetDescription("Downloading [package]");
                throw expected;
            }));

        Assert.Same(expected, actual);
        Assert.EndsWith(
            $"{failure} Installing workload{Environment.NewLine}  {gutter} Downloading [package]{Environment.NewLine}",
            stderr.ToString());
        Assert.DoesNotContain("\u2713", stderr.ToString());
        Assert.DoesNotContain("[x]", stderr.ToString());
    }

    [Fact]
    public async Task Progress_CancellationRetainsDetailWithoutSuccess()
    {
        var (service, _, stderr) = CreateService(interactive: true);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunWithProgressAsync<int>("Installing workload", (progress, ct) =>
            {
                Assert.Equal(cts.Token, ct);
                progress.SetDescription("Downloading");
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(0);
            }, cts.Token));

        Assert.EndsWith(
            $"\u00d7 Installing workload{Environment.NewLine}  \u2514 Downloading{Environment.NewLine}",
            stderr.ToString());
        Assert.DoesNotContain("\u2713", stderr.ToString());
    }

    [Fact]
    public async Task Progress_PreCancelledDoesNotStartActionOrDisplay()
    {
        var (service, stdout, stderr) = CreateService(interactive: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool invoked = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunWithProgressAsync("Installing", (_, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            }, cts.Token));

        Assert.False(invoked);
        Assert.Empty(stdout.ToString());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public async Task Progress_DeterminateUpdatesRemainAvailableAndCanReturnToIndeterminate()
    {
        var (service, _, stderr) = CreateService(interactive: true);

        await service.RunWithProgressAsync("Installing", (progress, _) =>
        {
            progress.SetTotal(100);
            progress.Report(40);
            progress.Increment(10);
            progress.SetDescription("Downloading");
            Assert.Contains("50%", stderr.ToString());

            stderr.GetStringBuilder().Clear();
            progress.SetTotal(null);
            progress.Report(90);
            progress.Increment(10);
            progress.SetDescription("Extracting");
            Assert.DoesNotContain("%", stderr.ToString());
            return Task.FromResult(0);
        });
    }

    [Fact]
    public async Task Progress_NonInteractiveLogsDistinctPhasesOnlyToStderr()
    {
        var (service, stdout, stderr) = CreateService(interactive: false);

        await service.RunWithProgressAsync("Installing", (progress, _) =>
        {
            progress.SetDescription("Installing");
            progress.SetDescription("Downloading");
            progress.SetDescription("Downloading");
            progress.SetTotal(100);
            progress.Report(50);
            progress.Increment(10);
            progress.SetDescription("Extracting");
            return Task.FromResult(0);
        });

        Assert.Equal(
            $"Installing...{Environment.NewLine}Downloading...{Environment.NewLine}Extracting...{Environment.NewLine}",
            stderr.ToString());
        Assert.Empty(stdout.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatusWrappersUseTheSameCompletionTreatment(bool generic)
    {
        var (service, stdout, stderr) = CreateService(interactive: true);

        if (generic)
        {
            int result = await service.ShowStatusAsync("Fetching catalog...", _ => Task.FromResult(42));
            Assert.Equal(42, result);
        }
        else
        {
            await service.StatusAsync("Fetching catalog...", _ => Task.CompletedTask);
        }

        Assert.EndsWith($"\u2713 Fetching catalog...{Environment.NewLine}", stderr.ToString());
        Assert.DoesNotContain("\u2514", stderr.ToString());
        Assert.Empty(stdout.ToString());
    }

    [Fact]
    public async Task Status_NonInteractivePreservesPlainLogging()
    {
        var (service, stdout, stderr) = CreateService(interactive: false);

        await service.StatusAsync("Fetching catalog", _ => Task.CompletedTask);

        Assert.Equal($"Fetching catalog...{Environment.NewLine}", stderr.ToString());
        Assert.Empty(stdout.ToString());
    }

    [Fact]
    public async Task Progress_DisplayFinishesBeforeNextOperation()
    {
        var (service, _, stderr) = CreateService(interactive: true);

        await service.StatusAsync("First", _ => Task.CompletedTask);
        await service.StatusAsync("Second", _ => Task.CompletedTask);

        Assert.Contains($"\u2713 First{Environment.NewLine}", stderr.ToString());
        Assert.EndsWith($"\u2713 Second{Environment.NewLine}", stderr.ToString());
        Assert.Equal(0, GetRenderedRow(stderr.GetStringBuilder().ToString(), "First"));
        Assert.Equal(1, GetRenderedRow(stderr.GetStringBuilder().ToString(), "Second"));
    }

    [Fact]
    public async Task Progress_CallerOwnedCompletionDoesNotAddSuccessRow()
    {
        var (service, _, stderr) = CreateService(interactive: true);

        await service.RunWithProgressAsync("Updating workload", (_, _) => Task.FromResult(0),
            writeCompletion: false);
        await service.StatusAsync("Scaffolding", _ => Task.CompletedTask, writeCompletion: false);

        Assert.DoesNotContain("\u2713", stderr.ToString());
    }

    [Fact]
    public async Task Progress_CallerOwnedCompletionStillRetainsFailure()
    {
        var (service, _, stderr) = CreateService(interactive: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunWithProgressAsync<int>("Installing", (progress, _) =>
            {
                progress.SetDescription("Extracting");
                throw new InvalidOperationException("Invalid package");
            }, writeCompletion: false));

        Assert.EndsWith($"\u00d7 Installing{Environment.NewLine}  \u2514 Extracting{Environment.NewLine}", stderr.ToString());
    }

    [Fact]
    public async Task Progress_RedirectedStdoutRemainsNonInteractiveWhenStderrIsTerminal()
    {
        var stdout = new StringWriter();
        var stderr = new CaptureWriter();
        var service = new SpectreInteractionService(
            new DefaultTheme(),
            CreateConsole(stdout, interactive: false, unicode: true),
            CreateConsole(stderr, interactive: true, unicode: true));

        await service.StatusAsync("Fetching catalog", _ => Task.CompletedTask);

        Assert.False(service.IsInteractive);
        Assert.Equal($"Fetching catalog...{Environment.NewLine}", stderr.ToString());
        Assert.Empty(stdout.ToString());
    }

    private static (SpectreInteractionService Service, StringWriter Stdout, StringWriter Stderr) CreateService(
        bool interactive, bool unicode = true)
    {
        var stdout = new StringWriter();
        var stderr = new CaptureWriter();
        IAnsiConsole output = CreateConsole(stdout, interactive, unicode);
        IAnsiConsole error = CreateConsole(stderr, interactive, unicode);
        return (new SpectreInteractionService(new DefaultTheme(), output, error), stdout, stderr);
    }

    private static IAnsiConsole CreateConsole(StringWriter writer, bool interactive, bool unicode)
    {
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = interactive ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });
        console.Profile.Capabilities.Unicode = unicode;
        console.Profile.Width = 120;
        return console;
    }

    private static int GetRenderedRow(string output, string text)
    {
        int textIndex = output.LastIndexOf(text, StringComparison.Ordinal);
        Assert.True(textIndex >= 0, $"Expected rendered text '{text}'.");
        int row = 0;

        // These short rows do not wrap; only newlines and vertical cursor movements change their position.
        foreach (Match movement in Regex.Matches(output[..textIndex], @"\n|\x1b\[(\d*)([ABEF])"))
        {
            int distance = movement.Groups[1].Value is ""
                ? 1 : Math.Max(1, int.Parse(movement.Groups[1].Value, CultureInfo.InvariantCulture));
            row = Math.Max(0, row + (movement.Groups[2].Value is "A" or "F" ? -distance : distance));
        }

        return row;
    }

    private sealed class CaptureWriter : StringWriter
    {
        public override string ToString()
            => Regex.Replace(base.ToString(), @"\x1b\[[0-?]*[ -/]*[@-~]", string.Empty);
    }
}
