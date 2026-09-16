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
/// Errors, warnings, status, progress, selection prompts, and text prompts go to
/// stderr. Confirmation prompts and other output go to stdout.
/// </para>
/// </remarks>
internal interface IInteractionService
{
    /// <summary>Active visual theme. Exposed so callers can use ad-hoc styles where needed.</summary>
    public ITheme Theme { get; }

    /// <summary>
    /// True when both output consoles support input and ANSI, so commands can combine
    /// stdout rendering with stderr selection prompts. Individual prompts check their target console.
    /// </summary>
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

    /// <summary>
    /// Serializes <paramref name="value"/> to indented JSON and writes it to
    /// stdout as a single block. Intended for the <c>--json</c> flag on
    /// commands that surface machine-readable output. Bypasses theming
    /// because JSON is data, not styled text.
    /// </summary>
    public void WriteJson(object value);

    /// <summary>Displays a status spinner while executing an async operation.</summary>
    public Task<T> ShowStatusAsync<T>(string statusMessage, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

    /// <summary>Displays a status spinner while executing an async operation (no return value).</summary>
    public Task StatusAsync(string statusMessage, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Displays a progress indicator (a bar in interactive contexts, plain
    /// log lines elsewhere) and passes a mutable <see cref="IProgressContext"/>
    /// to <paramref name="action"/> so the running operation can update its
    /// description and report completion.
    /// </summary>
    /// <remarks>
    /// Prefer this over <see cref="ShowStatusAsync{T}"/> when the operation
    /// has distinct phases the user benefits from seeing (e.g. resolve →
    /// download → extract). Phases with a known total surface as a real
    /// percentage; phases without one render as indeterminate motion.
    /// </remarks>
    public Task<T> RunWithProgressAsync<T>(
        string initialDescription,
        Func<IProgressContext, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts on stdout for yes/no confirmation, without requiring ANSI. Returns
    /// <paramref name="defaultValue"/> when input is unavailable. Throws <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    public Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts on stdout with <paramref name="defaultValue"/> for an empty answer, or returns
    /// <paramref name="whenInputUnavailable"/> when input is unavailable. Does not require ANSI; cancellation and input errors propagate.
    /// </summary>
    public Task<bool> ConfirmAsync(string prompt, bool defaultValue, bool whenInputUnavailable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts on stderr to select from a list of choices. Returns the first choice
    /// (or an empty string for no choices) when stderr lacks input or ANSI support.
    /// Throws <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    public Task<string> PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts on stderr to select one or more values, using SPACE to toggle and ENTER
    /// to confirm. Requires a selection. Returns an empty list for no choices or when
    /// stderr lacks input or ANSI support. Throws <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    public Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts the user to select one or more values from a list of labelled choices.
    /// Each <see cref="MultiSelectionChoice"/> carries a <see cref="MultiSelectionChoice.Label"/>
    /// shown to the user and a <see cref="MultiSelectionChoice.Value"/> returned in the
    /// result. Requires a selection. Returns an empty list for no choices or when
    /// stderr lacks input or ANSI support. Throws <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    public Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(string title, IEnumerable<MultiSelectionChoice> choices, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts on stderr for free-form text without requiring ANSI. Returns
    /// <paramref name="defaultValue"/> (or an empty string) when input is unavailable.
    /// Throws <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    public Task<string> PromptForInputAsync(string prompt, string? defaultValue = null, CancellationToken cancellationToken = default);
}
