// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Console;

internal sealed class OperationProgressDisplay(string title, ProgressTask task, ITheme theme, bool unicode) : Renderable
{
    private readonly Spinner _spinner = unicode ? Spinner.Known.Dots : Spinner.Known.Line;
    private readonly ProgressBarColumn _bar = new() { Width = 30 };

    protected override Measurement Measure(RenderOptions options, int maxWidth)
        => BuildProgress(options).Measure(options, maxWidth);

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        => BuildProgress(options).Render(options, maxWidth);

    private IRenderable BuildProgress(RenderOptions options)
    {
        int frame = (int)(task.ElapsedTime?.TotalMilliseconds / _spinner.Interval.TotalMilliseconds ?? 0);
        var rows = new List<IRenderable>
        {
            BuildTitle(_spinner.Frames[frame % _spinner.Frames.Count], theme.Active),
        };

        AddDetail(rows);

        if (!task.IsIndeterminate)
        {
            rows.Add(new Columns(
                new Text("  "),
                _bar.Render(options, task, TimeSpan.Zero),
                new Text($"{task.Percentage,3:0}%", theme.Muted)));
        }

        if (rows.Count == 1)
        {
            // Spectre 0.49.1 emits CSI 0 A for a single live row, which terminals interpret as moving up one row.
            rows.Add(new Text(" "));
        }

        return new Rows(rows);
    }

    internal IRenderable BuildResult(bool succeeded)
    {
        var rows = new List<IRenderable>
        {
            BuildTitle(
                succeeded ? unicode ? "\u2713" : "[x]" : unicode ? "\u00d7" : "[!]",
                succeeded ? theme.Success : theme.Error),
        };

        if (!succeeded)
        {
            AddDetail(rows);
        }

        return new Rows(rows);
    }

    private Paragraph BuildTitle(string icon, Style style)
        => new Paragraph().Append(icon, style).Append(" ").Append(title);

    private void AddDetail(List<IRenderable> rows)
    {
        string description = Markup.Remove(task.Description);
        if (!string.Equals(description, title, StringComparison.Ordinal))
        {
            rows.Add(new Text($"  {(unicode ? "\u2514" : "`")} {description}", theme.Muted));
        }
    }
}
