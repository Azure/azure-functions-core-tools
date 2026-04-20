// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Spectre.Console;

namespace Azure.Functions.Cli.Console;

/// <summary>
/// Rich console interaction using Spectre.Console, implementing both output
/// and interactive prompt capabilities.
/// </summary>
public class SpectreInteractionService : IInteractionService
{
    private readonly IAnsiConsole _stdout;
    private readonly IAnsiConsole _stderr;

    public SpectreInteractionService(IAnsiConsole? stdout = null, IAnsiConsole? stderr = null)
    {
        _stdout = stdout ?? AnsiConsole.Console;
        _stderr = stderr ?? AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(System.Console.Error)
        });
    }

    public bool IsInteractive =>
        !System.Console.IsInputRedirected &&
        !System.Console.IsOutputRedirected &&
        Environment.GetEnvironmentVariable("CI") is null;

    // --- Output (to stdout) ---

    public void WriteLine(string text) => _stdout.WriteLine(text);

    public void WriteMarkup(string markup) => _stdout.Markup(markup);

    public void WriteMarkupLine(string markup) => _stdout.MarkupLine(markup);

    public void WriteError(string message) =>
        _stderr.MarkupLine($"[red bold]Error:[/] [red]{message.EscapeMarkup()}[/]");

    public void WriteWarning(string message) =>
        _stderr.MarkupLine($"[yellow bold]Warning:[/] [yellow]{message.EscapeMarkup()}[/]");

    public void WriteSuccess(string message) =>
        _stdout.MarkupLine($"[green bold]✓[/] [green]{message.EscapeMarkup()}[/]");

    public void WriteTable(string[] columns, IEnumerable<string[]> rows)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        foreach (var column in columns)
        {
            table.AddColumn(new TableColumn(column).Centered());
        }

        foreach (var row in rows)
        {
            table.AddRow(row.Select(cell => new Markup(cell)).ToArray());
        }

        _stdout.Write(table);
    }

    public void WriteRule(string title) =>
        _stdout.Write(new Rule($"[blue]{title.EscapeMarkup()}[/]").LeftJustified());

    public void WriteBlankLine() => _stdout.WriteLine();

    // --- Interactive ---

    public async Task<T> ShowStatusAsync<T>(string statusMessage, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (!IsInteractive)
        {
            _stderr.MarkupLine($"[grey]{statusMessage.EscapeMarkup()}...[/]");
            return await action(cancellationToken);
        }

        T result = default!;
        await _stderr.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(statusMessage, async _ =>
            {
                result = await action(cancellationToken);
            });

        return result;
    }

    public async Task StatusAsync(string statusMessage, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await ShowStatusAsync(statusMessage, async ct =>
        {
            await action(ct);
            return 0;
        }, cancellationToken);
    }

    public async Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default)
    {
        if (!IsInteractive)
        {
            return defaultValue;
        }

        return await new ConfirmationPrompt(prompt)
        {
            DefaultValue = defaultValue
        }.ShowAsync(_stderr, cancellationToken);
    }

    public async Task<string> PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default)
    {
        var choiceList = choices.ToList();

        if (!IsInteractive || choiceList.Count == 0)
        {
            return choiceList.FirstOrDefault() ?? string.Empty;
        }

        return await new SelectionPrompt<string>()
            .Title(title)
            .AddChoices(choiceList)
            .ShowAsync(_stderr, cancellationToken);
    }

    public async Task<string> PromptForInputAsync(string prompt, string? defaultValue = null, CancellationToken cancellationToken = default)
    {
        if (!IsInteractive)
        {
            return defaultValue ?? string.Empty;
        }

        var textPrompt = new TextPrompt<string>(prompt)
            .AllowEmpty();

        if (defaultValue is not null)
        {
            textPrompt.DefaultValue(defaultValue);
        }

        var result = await textPrompt.ShowAsync(_stderr, cancellationToken);
        return string.IsNullOrEmpty(result) && defaultValue is not null
            ? defaultValue
            : result;
    }
}
