// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

internal readonly record struct LogTextPosition(long EntryId, int Offset) : IComparable<LogTextPosition>
{
    public int CompareTo(LogTextPosition other)
        => EntryId == other.EntryId ? Offset.CompareTo(other.Offset) : EntryId.CompareTo(other.EntryId);
}

internal sealed record LogFilter(string? Function = null, bool ErrorsOnly = false, LogLevel MinimumLevel = LogLevel.Information)
{
    public bool Includes(CompactLogLine line)
        => (Function is null || line.FunctionName is null || string.Equals(Function, line.FunctionName, StringComparison.Ordinal))
            && (!ErrorsOnly || line.IsError) && line.Level >= MinimumLevel;
}

internal sealed record LogCopySnapshot(long SelectionVersion, string Text);

/// <summary>
/// Maintains a logical reading/selection position independently of terminal rows and incoming logs.
/// </summary>
internal sealed class LogViewport
{
    private readonly List<Projection> _entries = [];
    private long _revision = -1;
    private LogFilter _filter = new();
    private LogTextPosition? _anchor;
    private LogTextPosition? _selectionStart;
    private LogTextPosition? _selectionEnd;
    private (LogTextPosition Before, LogTextPosition After)? _dragAnchor;
    private long _readThrough;
    private int _width;

    public int Height { get; private set; } = 1;
    public int TotalRows { get; private set; }
    public int TopRow { get; private set; }
    public int ScrollOffset => Math.Max(0, TotalRows - Height - TopRow);
    public int MaximumScroll => Math.Max(0, TotalRows - Height);
    public bool Following { get; private set; } = true;
    public bool SelectionExpired { get; private set; }
    public bool HasSelection => _selectionStart is { } start && _selectionEnd is { } end && start.CompareTo(end) < 0;
    public bool IsDragging => _dragAnchor is not null;
    public long SelectionVersion { get; private set; }
    public long Evicted { get; private set; }
    public int UnreadEntries => Following ? 0 : _entries.Count(entry => entry.Entry.Id > _readThrough);

    public void Refresh(LogBufferSnapshot buffer, int width, int height, LogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(filter);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        bool filterChanged = _filter != filter;
        if (filterChanged)
        {
            Follow();
        }

        if (_revision == buffer.Revision && _width == width && Height == height && !filterChanged)
        {
            return;
        }

        if (_width != width)
        {
            EndDrag();
        }

        _filter = filter;
        _revision = buffer.Revision;
        _width = width;
        Height = height;
        Evicted = buffer.Evicted;
        _entries.Clear();
        TotalRows = 0;
        foreach (BufferedLogEntry entry in buffer.Entries)
        {
            if (!filter.Includes(entry.Line))
            {
                continue;
            }

            LogRowIndex index = entry.Line.GetIndex(width);
            _entries.Add(new Projection(entry, index, TotalRows));
            TotalRows += index.Count;
        }

        if (HasSelection && (!Contains(_selectionStart!.Value.EntryId) || !Contains(_selectionEnd!.Value.EntryId)))
        {
            ClearSelection();
            SelectionExpired = true;
        }

        if (Following)
        {
            TopRow = MaximumScroll;
            _readThrough = _entries.Count == 0 ? 0 : _entries[^1].Entry.Id;
        }
        else if (_anchor is { } anchor)
        {
            Projection? entry = _entries.Find(item => item.Entry.Id >= anchor.EntryId);
            TopRow = entry is null ? MaximumScroll
                : Math.Clamp(entry.FirstRow + (entry.Entry.Id == anchor.EntryId ? entry.Index.FindRow(anchor.Offset) : 0), 0, MaximumScroll);
        }

        SaveAnchor();
    }

    public void Scroll(int rows)
    {
        TopRow = (int)Math.Clamp((long)TopRow - rows, 0, MaximumScroll);
        Following = TopRow == MaximumScroll && !HasSelection && !SelectionExpired && !IsDragging;
        if (Following)
        {
            _readThrough = _entries.Count == 0 ? 0 : _entries[^1].Entry.Id;
        }

        SaveAnchor();
    }

    public void Follow()
    {
        ClearSelection();
        Following = true;
        TopRow = MaximumScroll;
        _anchor = null;
        _readThrough = _entries.Count == 0 ? 0 : _entries[^1].Entry.Id;
    }

    public void Pause()
    {
        Following = false;
        SaveAnchor();
    }

    public IReadOnlyList<IRenderable> RenderRows(Style selectionStyle)
    {
        List<IRenderable> rows = [];
        for (int row = TopRow; row < Math.Min(TotalRows, TopRow + Height); row++)
        {
            (Projection entry, LogRowRange range) = GetRow(row);
            int start = -1;
            int end = -1;
            if (_selectionStart is { } selectionStart && _selectionEnd is { } selectionEnd
                && entry.Entry.Id >= selectionStart.EntryId && entry.Entry.Id <= selectionEnd.EntryId)
            {
                start = entry.Entry.Id == selectionStart.EntryId ? selectionStart.Offset : 0;
                end = entry.Entry.Id == selectionEnd.EntryId ? selectionEnd.Offset : entry.Entry.Line.Text.Length;
            }

            rows.Add(new LogRowRenderable(entry.Entry.Line, range, start, end, selectionStyle));
        }

        return rows;
    }

    public void BeginDrag(int x, int y)
    {
        ClearSelection();
        _dragAnchor = Hit(x, y);
        Pause();
    }

    public void Drag(int x, int y)
    {
        if (_dragAnchor is not { } anchor || Hit(x, y) is not { } focus)
        {
            return;
        }

        _selectionStart = anchor.Before.CompareTo(focus.Before) <= 0 ? anchor.Before : focus.Before;
        _selectionEnd = anchor.After.CompareTo(focus.After) >= 0 ? anchor.After : focus.After;
        SelectionVersion++;
    }

    public void EndDrag() => _dragAnchor = null;

    public void ClearSelection()
    {
        _selectionStart = null;
        _selectionEnd = null;
        _dragAnchor = null;
        SelectionExpired = false;
        SelectionVersion++;
    }

    public LogCopySnapshot CaptureSelection()
    {
        if (SelectionExpired)
        {
            throw new InvalidOperationException("The selected logs expired from history. Select the text again, or press Esc to clear.");
        }

        if (_selectionStart is not { } start || _selectionEnd is not { } end)
        {
            throw new InvalidOperationException("No log text is selected.");
        }

        StringBuilder text = new();
        bool first = true;
        foreach (Projection entry in _entries)
        {
            if (entry.Entry.Id < start.EntryId || entry.Entry.Id > end.EntryId)
            {
                continue;
            }

            if (!first)
            {
                text.Append('\n');
            }

            first = false;
            int from = entry.Entry.Id == start.EntryId ? start.Offset : 0;
            int to = entry.Entry.Id == end.EntryId ? end.Offset : entry.Entry.Line.Text.Length;
            text.Append(entry.Entry.Line.Text.AsSpan(from, to - from));
        }

        return new LogCopySnapshot(SelectionVersion, text.ToString());
    }

    public void CompleteCopy(long version, bool confirmed)
    {
        if (confirmed && SelectionVersion == version)
        {
            ClearSelection();
        }
    }

    private bool Contains(long id) => _entries.Exists(entry => entry.Entry.Id == id);

    private void SaveAnchor()
    {
        if (!Following && TotalRows > 0)
        {
            (Projection entry, LogRowRange range) = GetRow(TopRow);
            _anchor = new LogTextPosition(entry.Entry.Id, range.Start);
        }
    }

    private (Projection Entry, LogRowRange Range) GetRow(int row)
    {
        int low = 0;
        int high = _entries.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_entries[middle].FirstRow <= row)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        Projection entry = _entries[low];
        return (entry, entry.Index.GetRow(row - entry.FirstRow));
    }

    private (LogTextPosition Before, LogTextPosition After)? Hit(int x, int y)
    {
        if (TotalRows == 0)
        {
            return null;
        }

        int visualRow = Math.Clamp(TopRow + y, TopRow, Math.Min(TotalRows - 1, TopRow + Height - 1));
        (Projection entry, LogRowRange range) = GetRow(visualRow);
        int column = range.Indent;
        for (int offset = range.Start; offset < range.End;)
        {
            int length = LogText.ElementLength(entry.Entry.Line.Text, offset);
            int cells = LogText.CellWidth(entry.Entry.Line.Text, offset, length, column);
            if (x < column + Math.Max(1, cells))
            {
                return (new LogTextPosition(entry.Entry.Id, offset), new LogTextPosition(entry.Entry.Id, offset + length));
            }

            column += cells;
            offset += length;
        }

        LogTextPosition end = new(entry.Entry.Id, range.End);
        return (end, end);
    }

    private sealed record Projection(BufferedLogEntry Entry, LogRowIndex Index, int FirstRow);
}
