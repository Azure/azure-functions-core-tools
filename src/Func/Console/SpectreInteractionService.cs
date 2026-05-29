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
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        _stdout = stdout ?? AnsiConsole.Console;
        _stderr = stderr ?? AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(System.Console.Error)
        });
    }

    public ITheme Theme => _theme;

    public bool IsInteractive => AnsiConsole.Profile.Capabilities.Interactive;

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
        _stdout.Write(new Paragraph(title, _theme.Heading).Append(Environment.NewLine));

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
        Grid grid = new Grid()
            .AddColumn(new GridColumn().PadLeft(2).PadRight(4).NoWrap())
            .AddColumn(new GridColumn().PadRight(0));

        foreach (DefinitionItem item in items)
        {
            grid.AddRow(
                new Text(item.Label, _theme.Command),
                new Text(item.Description, _theme.Muted));
        }

        _stdout.Write(grid);
    }

    public void WriteTable(string[] columns, IEnumerable<string[]> rows)
    {
        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        foreach (string column in columns)
        {
            table.AddColumn(new TableColumn(new Text(column, _theme.Heading)));
        }

        foreach (string[] row in rows)
        {
            table.AddRow(row.Select(cell => (IRenderable)new Text(cell)).ToArray());
        }

        _stdout.Write(table);
    }

    public void WriteJson(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Bypass _stdout (which would re-render via Spectre) and write the
        // raw JSON directly so consumers piping `--json` get clean output.
        string json = System.Text.Json.JsonSerializer.Serialize(value, _jsonOptions);
        System.Console.Out.WriteLine(json);
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

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

    public async Task<T> RunWithProgressAsync<T>(
        string initialDescription,
        Func<IProgressContext, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialDescription);
        ArgumentNullException.ThrowIfNull(action);

        if (!IsInteractive)
        {
            // No live region in non-interactive output. Log the initial
            // description plus every subsequent SetDescription so the user
            // still sees phase transitions in CI logs.
            _stderr.Write(new Paragraph($"{initialDescription}...", _theme.Muted).Append(Environment.NewLine));
            var logging = new LoggingProgressContext(_stderr, _theme, initialDescription);
            return await action(logging, cancellationToken);
        }

        T result = default!;
        await _stderr.Progress()
            .AutoRefresh(true)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(Spinner.Known.Dots))
            .StartAsync(async ctx =>
            {
                ProgressTask task = ctx.AddTask(initialDescription, autoStart: true, maxValue: 1d);
                task.IsIndeterminate = true;

                var live = new SpectreProgressContext(task);
                result = await action(live, cancellationToken);

                // Snap to 100% on success so the bar doesn't linger as a
                // half-filled fragment when the action finishes without
                // having called Report.
                if (task.IsIndeterminate)
                {
                    task.IsIndeterminate = false;
                    task.MaxValue = 1d;
                    task.Value = 1d;
                }
                else if (task.MaxValue > 0)
                {
                    task.Value = task.MaxValue;
                }

                task.StopTask();
            });

        return result;
    }

    private sealed class SpectreProgressContext(ProgressTask task) : IProgressContext
    {
        private readonly ProgressTask _task = task;

        public void SetDescription(string description)
        {
            ArgumentNullException.ThrowIfNull(description);
            _task.Description = description;
        }

        public void SetTotal(double? total)
        {
            if (total is null)
            {
                _task.IsIndeterminate = true;
                return;
            }

            _task.IsIndeterminate = false;
            _task.MaxValue = total.Value;
            if (_task.Value > total.Value)
            {
                _task.Value = total.Value;
            }
        }

        public void Report(double value)
        {
            if (_task.IsIndeterminate)
            {
                return;
            }

            _task.Value = value;
        }

        public void Increment(double amount)
        {
            if (_task.IsIndeterminate)
            {
                return;
            }

            _task.Increment(amount);
        }
    }

    private sealed class LoggingProgressContext(IAnsiConsole console, ITheme theme, string initialDescription) : IProgressContext
    {
        private readonly IAnsiConsole _console = console;
        private readonly ITheme _theme = theme;
        private string _description = initialDescription;

        public void SetDescription(string description)
        {
            ArgumentNullException.ThrowIfNull(description);
            if (string.Equals(description, _description, StringComparison.Ordinal))
            {
                return;
            }

            _description = description;
            _console.Write(new Paragraph($"{description}...", _theme.Muted).Append(Environment.NewLine));
        }

        public void SetTotal(double? total)
        {
        }

        public void Report(double value)
        {
        }

        public void Increment(double amount)
        {
        }
    }

    public async Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default)
    {
        if (!IsInteractive)
        {
            return defaultValue;
        }

        return await new ConfirmationPrompt(prompt) { DefaultValue = defaultValue }
            .ShowAsync(_stdout, cancellationToken);
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
            .EnableSearch()
            .AddChoices(choiceList)
            .ShowAsync(_stderr, cancellationToken);
    }

    public Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default)
        => PromptForMultiSelectionAsync(title, choices.Select(static c => new MultiSelectionChoice(c)), cancellationToken);

    public async Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<MultiSelectionChoice> choices, CancellationToken cancellationToken = default)
    {
        var choiceList = choices.ToList();
        if (!IsInteractive || choiceList.Count == 0)
        {
            return [];
        }

        // NotRequired() lets the user press ENTER with nothing selected to exit
        // the prompt cleanly. Callers treat an empty result as "no selection",
        // which (for `func setup`) is the documented escape hatch from the
        // stack picker.
        List<MultiSelectionChoice> selected = await new MultiSelectionPrompt<MultiSelectionChoice>()
            .Title(title)
            .NotRequired()
            .InstructionsText("[grey](press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm; ENTER with no selection exits)[/]")
            .UseConverter(static choice => choice.Label)
            .AddChoices(choiceList)
            .ShowAsync(_stderr, cancellationToken);

        return [.. selected.Select(static choice => choice.Value)];
    }

    public async Task<string> PromptForInputAsync(string prompt, string? defaultValue = null, CancellationToken cancellationToken = default)
    {
        if (!IsInteractive)
        {
            return defaultValue ?? string.Empty;
        }

        TextPrompt<string> textPrompt = new TextPrompt<string>(prompt).AllowEmpty();
        if (defaultValue is not null)
        {
            textPrompt.DefaultValue(defaultValue);
        }

        string result = await textPrompt.ShowAsync(_stderr, cancellationToken);
        return string.IsNullOrEmpty(result) && defaultValue is not null ? defaultValue : result;
    }
}
