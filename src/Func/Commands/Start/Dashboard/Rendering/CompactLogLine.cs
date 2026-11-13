// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Immutable selectable display text, independent of wrapping and terminal state.
/// </summary>
internal sealed class CompactLogLine
{
    private readonly LogStyleSpan[] _styles;
    private readonly int _continuationIndent;
    private LogRowIndex? _index;

    public CompactLogLine(IRenderable renderable, string? functionName, bool isError, LogLevel level, int continuationIndent = 0)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        RenderOptions options = LogText.CreateRenderOptions(int.MaxValue, int.MaxValue);
        StringBuilder text = new();
        List<LogStyleSpan> styles = [];
        foreach (Segment segment in renderable.Render(options, int.MaxValue))
        {
            if (segment.IsControlCode)
            {
                continue;
            }

            string value = LogText.Sanitize(segment.Text);
            if (styles.Count > 0 && styles[^1].Style.Equals(segment.Style))
            {
                styles[^1] = styles[^1] with { Length = styles[^1].Length + value.Length };
            }
            else
            {
                styles.Add(new LogStyleSpan(text.Length, value.Length, segment.Style));
            }

            text.Append(value);
        }

        Text = text.ToString();
        _styles = [.. styles];
        _continuationIndent = Math.Max(0, continuationIndent);
        FunctionName = functionName;
        IsError = isError;
        Level = level;
    }

    private CompactLogLine(string text, LogStyleSpan[] styles, CompactLogLine original)
    {
        Text = text;
        _styles = styles;
        _continuationIndent = original._continuationIndent;
        FunctionName = original.FunctionName;
        IsError = original.IsError;
        Level = original.Level;
    }

    public string Text { get; }
    public string? FunctionName { get; }
    public bool IsError { get; }
    public LogLevel Level { get; }
    public long TextBytes => ((long)Text.Length + (FunctionName?.Length ?? 0)
        + _styles.Select(span => span.Style.Link).Distinct(StringComparer.Ordinal).Sum(link => (long)(link?.Length ?? 0))) * sizeof(char);
    public IRenderable Renderable => new EntryRenderable(this);

    public IReadOnlyList<IRenderable> RenderRows(int width)
    {
        LogRowIndex index = GetIndex(width);
        return [.. Enumerable.Range(0, index.Count).Select(row => (IRenderable)new LogRowRenderable(this, index.GetRow(row)))];
    }

    public LogRowIndex GetIndex(int width)
    {
        width = Math.Max(1, width);
        LogRowIndex? index = _index;
        if (index is null || index.Width != width)
        {
            index = new LogRowIndex(Text, width, _continuationIndent);
            _index = index;
        }

        return index;
    }

    public CompactLogLine LimitText(int maxCharacters)
    {
        int budget = Math.Max(0, maxCharacters - (FunctionName?.Length ?? 0));
        if (Text.Length <= budget)
        {
            return this;
        }

        const string marker = " [display truncated]";
        int end = LogText.BoundaryBefore(Text, Math.Max(0, budget - marker.Length));
        string text = Text[..end] + marker[..Math.Min(marker.Length, budget - end)];
        return new CompactLogLine(text, [.. _styles.Where(style => style.Start < end)], this);
    }

    internal Style StyleAt(int offset)
    {
        foreach (LogStyleSpan span in _styles)
        {
            if (offset >= span.Start && offset < span.Start + span.Length)
            {
                return span.Style;
            }
        }

        return Style.Plain;
    }

    private readonly record struct LogStyleSpan(int Start, int Length, Style Style);

    private sealed class EntryRenderable(CompactLogLine line) : Renderable
    {
        protected override Measurement Measure(RenderOptions options, int maxWidth) => new(0, maxWidth);

        protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            LogRowIndex index = line.GetIndex(maxWidth);
            for (int row = 0; row < index.Count; row++)
            {
                if (row > 0)
                {
                    yield return Segment.LineBreak;
                }

                IRenderable renderable = new LogRowRenderable(line, index.GetRow(row));
                foreach (Segment segment in renderable.Render(options, maxWidth))
                {
                    yield return segment;
                }
            }
        }
    }
}

internal readonly record struct LogRowRange(int Start, int End, int Next, int Indent, bool Last);

/// <summary>
/// Indexes wrapped rows with sparse checkpoints rather than retaining every visual row.
/// </summary>
internal sealed class LogRowIndex
{
    private const int CheckpointStride = 128;
    private readonly string _text;
    private readonly int _indent;
    private readonly List<int> _checkpoints = [];

    public LogRowIndex(string text, int width, int continuationIndent)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        Width = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
        _indent = Math.Min(continuationIndent, width / 3);
        int start = 0;
        while (true)
        {
            if (Count % CheckpointStride == 0)
            {
                _checkpoints.Add(start);
            }

            LogRowRange row = ReadRow(start, Count == 0);
            Count++;
            if (row.Last)
            {
                break;
            }

            start = row.Next;
        }
    }

    public int Width { get; }
    public int Count { get; }

    public LogRowRange GetRow(int row)
    {
        row = Math.Clamp(row, 0, Count - 1);
        int first = row / CheckpointStride * CheckpointStride;
        int start = _checkpoints[row / CheckpointStride];
        for (int current = first; ; current++)
        {
            LogRowRange range = ReadRow(start, current == 0);
            if (current == row)
            {
                return range;
            }

            start = range.Next;
        }
    }

    public int FindRow(int offset)
    {
        int checkpoint = _checkpoints.BinarySearch(Math.Clamp(offset, 0, _text.Length));
        checkpoint = checkpoint >= 0 ? checkpoint : Math.Max(0, ~checkpoint - 1);
        int row = checkpoint * CheckpointStride;
        int start = _checkpoints[checkpoint];
        while (row < Count - 1)
        {
            LogRowRange range = ReadRow(start, row == 0);
            if (offset < range.Next || range.Last)
            {
                return row;
            }

            start = range.Next;
            row++;
        }

        return row;
    }

    private LogRowRange ReadRow(int start, bool first)
    {
        int indent = first ? 0 : _indent;
        int column = indent;
        int offset = start;
        while (offset < _text.Length)
        {
            if (_text[offset] == '\n')
            {
                return new LogRowRange(start, offset, offset + 1, indent, false);
            }

            int length = LogText.ElementLength(_text, offset);
            int cells = LogText.CellWidth(_text, offset, length, column);
            if (column + cells > Width && offset != start)
            {
                return new LogRowRange(start, offset, offset, indent, false);
            }

            offset += length;
            column += Math.Min(cells, Width - column);
        }

        return new LogRowRange(start, offset, offset, indent, true);
    }
}

internal sealed class LogRowRenderable(CompactLogLine line, LogRowRange range, int selectionStart = -1,
    int selectionEnd = -1, Style? selectionStyle = null) : Renderable
{
    private readonly CompactLogLine _line = line ?? throw new ArgumentNullException(nameof(line));

    protected override Measurement Measure(RenderOptions options, int maxWidth) => new(0, maxWidth);

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        int column = Math.Min(range.Indent, Math.Max(0, maxWidth - 1));
        if (column > 0)
        {
            yield return Segment.Padding(column);
        }

        for (int offset = range.Start; offset < range.End;)
        {
            int length = LogText.ElementLength(_line.Text, offset);
            int cells = LogText.CellWidth(_line.Text, offset, length, column);
            string text = _line.Text[offset] == '\t' ? new string(' ', Math.Min(cells, maxWidth - column)) : _line.Text.Substring(offset, length);
            if (column + cells > maxWidth)
            {
                text = "?";
                cells = 1;
            }

            bool selected = offset < selectionEnd && offset + length > selectionStart;
            yield return new Segment(text, selected ? selectionStyle ?? new Style(decoration: Decoration.Invert) : _line.StyleAt(offset));
            column += cells;
            offset += length;
        }
    }
}
