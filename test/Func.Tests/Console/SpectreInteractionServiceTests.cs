// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Console;

public class SpectreInteractionServiceTests
{
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
}