// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// A test interaction service that captures all output for assertions.
/// Interactive methods return defaults (non-interactive mode).
/// </summary>
public class TestInteractionService : IInteractionService
{
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines;
    public string AllOutput => string.Join(Environment.NewLine, _lines);
    public bool IsInteractive => false;

    public void WriteLine(string text) => _lines.Add(text);
    public void WriteMarkup(string markup) => _lines.Add(StripMarkup(markup));
    public void WriteMarkupLine(string markup) => _lines.Add(StripMarkup(markup));
    public void WriteError(string message) => _lines.Add($"ERROR: {message}");
    public void WriteWarning(string message) => _lines.Add($"WARNING: {message}");
    public void WriteSuccess(string message) => _lines.Add($"SUCCESS: {message}");

    public void WriteTable(string[] columns, IEnumerable<string[]> rows)
    {
        _lines.Add($"TABLE: [{string.Join(", ", columns)}]");
        foreach (var row in rows)
        {
            _lines.Add($"  ROW: [{string.Join(", ", row.Select(StripMarkup))}]");
        }
    }

    public void WriteRule(string title) => _lines.Add($"RULE: {StripMarkup(title)}");
    public void WriteBlankLine() => _lines.Add("");

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

    private static string StripMarkup(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[^\]]*\]", "");
    }
}
