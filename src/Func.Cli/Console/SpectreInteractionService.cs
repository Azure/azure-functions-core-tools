// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Console;

/// <summary>
/// Rich console implementation backed by Spectre.Console. All styled output
/// flows through <see cref="ITheme"/>.
/// </summary>
internal class SpectreInteractionService : IInteractionService
{
    private readonly IAnsiConsole _stdout;
    private readonly IAnsiConsole _stderr;
    private readonly ITheme _theme;

    public SpectreInteractionService(ITheme theme, IAnsiConsole? stdout = null, IAnsiConsole? stderr = null)
    {
        _theme = theme;
        _stdout = stdout ?? AnsiConsole.Console;
        _stderr = stderr ?? AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(System.Console.Error)
        });
    }

    public ITheme Theme => _theme;

    public bool IsInteractive =>
        !System.Console.IsInputRedirected &&
        !System.Console.IsOutputRedirected &&
        Environment.GetEnvironmentVariable("CI") is null;

    public void WriteLine(string text) => _stdout.WriteLine(text);

    public void WriteBlankLine() => _stdout.WriteLine();

    public void WriteLine(Action<InlineLine> build)
    {
        var line = new InlineLine(_theme);
        build(line);
        _stdout.Write(line.ToRenderable());
        _stdout.WriteLine();
    }

    public void Write(IRenderable renderable) => _stdout.Write(renderable);

    public void WriteTitle(string text) =>
        _stdout.Write(new Paragraph(text, _theme.Title).Append(Environment.NewLine));

    public void WriteSectionHeader(string title) =>
        _stdout.Write(new Rule(title).LeftJustified().RuleStyle(_theme.Heading));

    public void WriteHint(string message) =>
        _stdout.Write(new Paragraph(message, _theme.Muted).Append(Environment.NewLine));

    public void WriteSuccess(string message)
    {
        _stdout.Write(new Paragraph()
            .Append("✓ ", _theme.Success)
            .Append(message, _theme.Success)
            .Append(Environment.NewLine));
    }

    public void WriteError(string message)
    {
        _stderr.Write(new Paragraph()
            .Append("Error: ", _theme.Error)
            .Append(message, _theme.Error)
            .Append(Environment.NewLine));
    }

    public void WriteWarning(string message)
    {
        _stderr.Write(new Paragraph()
            .Append("Warning: ", _theme.Warning)
            .Append(message, _theme.Warning)
            .Append(Environment.NewLine));
    }

    public void WriteDefinitionList(IEnumerable<DefinitionItem> items)
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().PadLeft(2).PadRight(4).NoWrap())
            .AddColumn(new GridColumn().PadRight(0));

        foreach (var item in items)
        {
            grid.AddRow(
                new Text(item.Label, _theme.Command),
                new Text(item.Description, _theme.Muted));
        }

        _stdout.Write(grid);
    }

    public void WriteTable(string[] columns, IEnumerable<string[]> rows)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        foreach (var column in columns)
        {
            table.AddColumn(new TableColumn(new Text(column, _theme.Heading)).Centered());
        }

        foreach (var row in rows)
        {
            table.AddRow(row.Select(cell => (IRenderable)new Text(cell)).ToArray());
        }

        _stdout.Write(table);
    }

    public async Task<T> ShowStatusAsync<T>(string statusMessage, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (!IsInteractive)
        {
            _stderr.Write(new Paragraph($"{statusMessage}...", _theme.Muted).Append(Environment.NewLine));
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

        return await new ConfirmationPrompt(prompt) { DefaultValue = defaultValue }
            .ShowAsync(_stderr, cancellationToken);
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

        var textPrompt = new TextPrompt<string>(prompt).AllowEmpty();
        if (defaultValue is not null)
        {
            textPrompt.DefaultValue(defaultValue);
        }

        var result = await textPrompt.ShowAsync(_stderr, cancellationToken);
        return string.IsNullOrEmpty(result) && defaultValue is not null ? defaultValue : result;
    }
}
