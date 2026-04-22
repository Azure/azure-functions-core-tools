// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Test double that captures output as plain text for assertions.
/// Styles are discarded; semantic roles are captured as prefixes
/// (ERROR:, WARNING:, SUCCESS:, HINT:, TITLE:, RULE:, STATUS:…)
/// so tests can assert on both content and role.
/// Interactive methods return defaults (non-interactive mode).
/// </summary>
public class TestInteractionService : IInteractionService
{
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines;
    public string AllOutput => string.Join(Environment.NewLine, _lines);
    public bool IsInteractive => false;
    public ITheme Theme { get; } = new DefaultTheme();

    // --- Plain output ---

    public void WriteLine(string text) => _lines.Add(text);

    public void WriteBlankLine() => _lines.Add(string.Empty);

    // --- Composed styled output ---

    public void WriteLine(Action<InlineLine> build)
    {
        var line = new InlineLine(Theme);
        build(line);
        _lines.Add(line.ToPlainString());
    }

    public void Write(IRenderable renderable) => _lines.Add($"RENDERABLE: {renderable.GetType().Name}");

    // --- Semantic output ---

    public void WriteTitle(string text) => _lines.Add($"TITLE: {text}");

    public void WriteSectionHeader(string title) => _lines.Add($"RULE: {title}");

    public void WriteHint(string message) => _lines.Add($"HINT: {message}");

    public void WriteSuccess(string message) => _lines.Add($"SUCCESS: {message}");

    public void WriteError(string message) => _lines.Add($"ERROR: {message}");

    public void WriteWarning(string message) => _lines.Add($"WARNING: {message}");

    public void WriteDefinitionList(IEnumerable<DefinitionItem> items)
    {
        foreach (var item in items)
        {
            _lines.Add($"  {item.Label}    {item.Description}");
        }
    }

    public void WriteTable(string[] columns, IEnumerable<string[]> rows)
    {
        _lines.Add($"TABLE: [{string.Join(", ", columns)}]");
        foreach (var row in rows)
        {
            _lines.Add($"  ROW: [{string.Join(", ", row)}]");
        }
    }

    // --- Interactive ---

    public async Task<T> ShowStatusAsync<T>(string statusMessage, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lines.Add($"STATUS: {statusMessage}");
        return await action(cancellationToken);
    }

    public async Task StatusAsync(string statusMessage, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lines.Add($"STATUS: {statusMessage}");
        await action(cancellationToken);
    }

    public Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lines.Add($"CONFIRM: {prompt} (default: {defaultValue})");
        return Task.FromResult(defaultValue);
    }

    public Task<string> PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var choiceList = choices.ToList();
        _lines.Add($"SELECT: {title} [{string.Join(", ", choiceList)}]");
        return Task.FromResult(choiceList.FirstOrDefault() ?? string.Empty);
    }

    public Task<string> PromptForInputAsync(string prompt, string? defaultValue = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lines.Add($"INPUT: {prompt} (default: {defaultValue})");
        return Task.FromResult(defaultValue ?? string.Empty);
    }
}
