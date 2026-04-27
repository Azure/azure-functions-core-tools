// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Console;

/// <summary>
/// Abstraction over console interaction. Exposes semantic output methods,
/// theme-aware line composition, and interactive prompts.
/// </summary>
/// <remarks>
/// <para>
/// Callers must not write raw Spectre markup (<c>[red]…[/]</c>). Use role-named
/// helpers or compose with <see cref="WriteLine(Action{InlineLine})"/>.
/// </para>
/// <para>
/// Output routing: <see cref="WriteError"/> and <see cref="WriteWarning"/> go to
/// stderr; all other output goes to stdout.
/// </para>
/// </remarks>
internal interface IInteractionService
{
    /// <summary>Active visual theme. Exposed so callers can use ad-hoc styles where needed.</summary>
    public ITheme Theme { get; }

    /// <summary>True when the console supports interactive prompts (not redirected, not CI).</summary>
    public bool IsInteractive { get; }

    /// <summary>Writes a single line of unstyled text to stdout.</summary>
    public void WriteLine(string text);

    /// <summary>Writes a blank line to stdout.</summary>
    public void WriteBlankLine();

    /// <summary>
    /// Writes a styled line built via a fluent <see cref="InlineLine"/>. Example:
    /// <code>WriteLine(l =&gt; l.Muted("Run ").Command("func new").Muted(" to begin."));</code>
    /// </summary>
    public void WriteLine(Action<InlineLine> build);

    /// <summary>
    /// Writes any Spectre <see cref="IRenderable"/> (e.g. <c>Grid</c>, <c>Table</c>,
    /// <c>Panel</c>). The typed escape hatch for layout needs not covered by the
    /// semantic helpers.
    /// </summary>
    public void Write(IRenderable renderable);

    /// <summary>Writes a product or document title (e.g. the CLI banner line).</summary>
    public void WriteTitle(string text);

    /// <summary>Writes a section header rendered as a left-justified horizontal rule.</summary>
    public void WriteSectionHeader(string title);

    /// <summary>Writes an informational hint styled as muted text.</summary>
    public void WriteHint(string message);

    /// <summary>Writes a success marker (✓) followed by the message, to stdout.</summary>
    public void WriteSuccess(string message);

    /// <summary>Writes a red "Error:" prefix followed by the message, to stderr.</summary>
    public void WriteError(string message);

    /// <summary>Writes a yellow "Warning:" prefix followed by the message, to stderr.</summary>
    public void WriteWarning(string message);

    /// <summary>
    /// Writes an aligned list of label/description rows. Labels are styled with the
    /// theme's command role; descriptions with the muted role. Alignment is handled
    /// by Spectre internally — no manual padding.
    /// </summary>
    public void WriteDefinitionList(IEnumerable<DefinitionItem> items);

    /// <summary>Writes a bordered data table.</summary>
    public void WriteTable(string[] columns, IEnumerable<string[]> rows);

    /// <summary>Displays a status spinner while executing an async operation.</summary>
    public Task<T> ShowStatusAsync<T>(string statusMessage, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

    /// <summary>Displays a status spinner while executing an async operation (no return value).</summary>
    public Task StatusAsync(string statusMessage, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts the user with a yes/no confirmation. Returns <paramref name="defaultValue"/>
    /// in non-interactive mode. Throws <see cref="OperationCanceledException"/> on Ctrl+C.
    /// </summary>
    public Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts the user to select from a list of choices. Returns the first choice in
    /// non-interactive mode. Throws <see cref="OperationCanceledException"/> on Ctrl+C.
    /// </summary>
    public Task<string> PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts the user for free-form text with an optional default. Returns
    /// <paramref name="defaultValue"/> in non-interactive mode. Throws
    /// <see cref="OperationCanceledException"/> on Ctrl+C.
    /// </summary>
    public Task<string> PromptForInputAsync(string prompt, string? defaultValue = null, CancellationToken cancellationToken = default);
}
