// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console;

/// <summary>
/// Abstraction over console interaction for testability. Provides output methods
/// and interactive prompts (status spinners, confirmations, selections).
/// </summary>
public interface IInteractionService
{
    // --- Output ---
    public void WriteLine(string text);
    public void WriteMarkup(string markup);
    public void WriteMarkupLine(string markup);
    public void WriteError(string message);
    public void WriteWarning(string message);
    public void WriteSuccess(string message);
    public void WriteTable(string[] columns, IEnumerable<string[]> rows);
    public void WriteRule(string title);
    public void WriteBlankLine();

    // --- Interactive ---

    /// <summary>
    /// Displays a status spinner while executing an async operation.
    /// </summary>
    public Task<T> ShowStatusAsync<T>(string statusMessage, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Displays a status spinner while executing an async operation (no return value).
    /// </summary>
    public Task StatusAsync(string statusMessage, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts the user with a yes/no confirmation.
    /// Returns <paramref name="defaultValue"/> in non-interactive mode.
    /// Throws <see cref="OperationCanceledException"/> on Ctrl+C.
    /// </summary>
    public Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts the user to select from a list of choices.
    /// Returns the first choice in non-interactive mode.
    /// Throws <see cref="OperationCanceledException"/> on Ctrl+C.
    /// </summary>
    public Task<string> PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts the user for free-form text input with an optional default value.
    /// Returns <paramref name="defaultValue"/> in non-interactive mode.
    /// Throws <see cref="OperationCanceledException"/> on Ctrl+C.
    /// </summary>
    public Task<string> PromptForInputAsync(string prompt, string? defaultValue = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the console supports interactive prompts (not redirected, not CI).
    /// </summary>
    public bool IsInteractive { get; }
}
