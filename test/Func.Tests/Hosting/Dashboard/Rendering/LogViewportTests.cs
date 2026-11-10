// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class LogViewportTests
{
    [Fact]
    public void History_EnforcesCountAndTextBudget()
    {
        var buffer = new CompactLogBuffer(3, 60);
        for (int index = 0; index < 20; index++)
        {
            buffer.Add(Line("0123456789"));
        }

        var snapshot = buffer.Capture();
        snapshot.Entries.Should().HaveCount(3);
        snapshot.TextBytes.Should().Be(60);
        snapshot.Evicted.Should().Be(17);
        snapshot.Entries.Select(entry => entry.Id).Should().Equal(18, 19, 20);
    }

    [Fact]
    public void History_TruncatesOversizedDisplayEntry()
    {
        var buffer = new CompactLogBuffer(3, 80);
        buffer.Add(Line(new string('a', 1000)));

        var snapshot = buffer.Capture();
        snapshot.TextBytes.Should().BeLessThanOrEqualTo(80);
        snapshot.Entries.Single().Line.Text.Should().EndWith("[display truncated]");
    }

    [Fact]
    public void Scrolling_IsNotLimitedToEntryCount()
    {
        var buffer = new CompactLogBuffer();
        buffer.Add(Line(new string('x', 3000)));
        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 5, 5, new LogFilter());

        viewport.TotalRows.Should().Be(600);
        viewport.Scroll(1000);
        viewport.TopRow.Should().Be(0);
        viewport.ScrollOffset.Should().Be(595);
    }

    [Fact]
    public void IncomingEntries_KeepTheReadingAnchorAndCountUnread()
    {
        var buffer = new CompactLogBuffer();
        for (int index = 0; index < 30; index++)
        {
            buffer.Add(Line($"record {index}"));
        }

        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 40, 5, new LogFilter());
        viewport.Scroll(10);
        int top = viewport.TopRow;
        buffer.Add(Line("new entry"));
        viewport.Refresh(buffer.Capture(), 40, 5, new LogFilter());

        viewport.TopRow.Should().Be(top);
        viewport.UnreadEntries.Should().Be(1);
        viewport.Follow();
        viewport.UnreadEntries.Should().Be(0);
        viewport.ScrollOffset.Should().Be(0);
    }

    [Fact]
    public void Selection_CopiesLogicalTextAcrossSoftWrapsAndPreservesHardNewlines()
    {
        var buffer = new CompactLogBuffer();
        buffer.Add(Line("abcdefgh\nijk"));
        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 4, 8, new LogFilter());
        viewport.BeginDrag(1, 0);
        viewport.Drag(2, 2);
        viewport.EndDrag();

        viewport.CaptureSelection().Text.Should().Be("bcdefgh\nijk");
        viewport.Refresh(buffer.Capture(), 20, 8, new LogFilter());
        viewport.CaptureSelection().Text.Should().Be("bcdefgh\nijk");
    }

    [Fact]
    public void Selection_ExcludesHiddenEntriesAndHandlesReverseDrag()
    {
        var buffer = new CompactLogBuffer();
        buffer.Add(Line("alpha", "A"));
        buffer.Add(Line("hidden", "B"));
        buffer.Add(Line("omega", "A"));
        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 40, 8, new LogFilter("A"));
        viewport.BeginDrag(4, 1);
        viewport.Drag(0, 0);

        viewport.CaptureSelection().Text.Should().Be("alpha\nomega");
    }

    [Fact]
    public void Selection_EvictionExpiresRatherThanRetargets()
    {
        var buffer = new CompactLogBuffer(2);
        buffer.Add(Line("old"));
        buffer.Add(Line("current"));
        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 40, 8, new LogFilter());
        viewport.BeginDrag(0, 0);
        viewport.Drag(2, 0);
        viewport.EndDrag();
        buffer.Add(Line("new"));
        viewport.Refresh(buffer.Capture(), 40, 8, new LogFilter());

        viewport.HasSelection.Should().BeFalse();
        viewport.SelectionExpired.Should().BeTrue();
        FluentActions.Invoking(viewport.CaptureSelection).Should().Throw<InvalidOperationException>().WithMessage("*expired*");
        viewport.ClearSelection();
        viewport.SelectionExpired.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragAnchor_EvictionBeforeFirstMovementExpiresEvenDuringResize(bool resize)
    {
        var buffer = new CompactLogBuffer(1);
        buffer.Add(Line("old"));
        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 40, 8, new LogFilter());
        viewport.BeginDrag(0, 0);

        buffer.Add(Line("new"));
        int width = resize ? 80 : 40;
        viewport.Refresh(buffer.Capture(), width, 8, new LogFilter());
        viewport.Drag(2, 0);
        viewport.EndDrag();
        viewport.Refresh(buffer.Capture(), width, 8, new LogFilter());

        viewport.SelectionExpired.Should().BeTrue();
        viewport.HasSelection.Should().BeFalse();
        FluentActions.Invoking(viewport.CaptureSelection).Should().Throw<InvalidOperationException>().WithMessage("*expired*");
    }

    [Fact]
    public void Invalidation_PreservesTheReadingAnchorButRejectsStalePointerGeometry()
    {
        var buffer = new CompactLogBuffer(3);
        buffer.Add(Line("first"));
        buffer.Add(Line("second"));
        buffer.Add(Line("third"));
        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 40, 1, new LogFilter());
        viewport.Scroll(1);

        long oldest = buffer.Add(Line("fourth"))!.Value;
        viewport.Invalidate(oldest);
        viewport.BeginDrag(0, 0);
        viewport.Drag(3, 0);
        viewport.HasSelection.Should().BeFalse();
        viewport.Refresh(buffer.Capture(), 40, 1, new LogFilter());
        viewport.BeginDrag(0, 0);
        viewport.Drag(5, 0);

        viewport.CaptureSelection().Text.Should().Be("second");
    }

    [Fact]
    public void Invalidation_ExpiresTheDragAnchorWithoutNeedingARepaint()
    {
        var buffer = new CompactLogBuffer(1);
        buffer.Add(Line("old"));
        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 40, 1, new LogFilter());
        viewport.BeginDrag(0, 0);

        viewport.Invalidate(buffer.Add(Line("new"))!.Value);
        viewport.BeginDrag(0, 0);
        viewport.Drag(2, 0);

        viewport.SelectionExpired.Should().BeTrue();
        FluentActions.Invoking(viewport.CaptureSelection).Should().Throw<InvalidOperationException>().WithMessage("*expired*");
    }

    [Fact]
    public void CopyCompletion_DoesNotClearANewerSelection()
    {
        var buffer = new CompactLogBuffer();
        buffer.Add(Line("abcdefgh"));
        var viewport = new LogViewport();
        viewport.Refresh(buffer.Capture(), 40, 8, new LogFilter());
        viewport.BeginDrag(0, 0);
        viewport.Drag(2, 0);
        var captured = viewport.CaptureSelection();
        viewport.BeginDrag(4, 0);
        viewport.Drag(6, 0);
        viewport.CompleteCopy(captured.SelectionVersion, confirmed: true);

        viewport.CaptureSelection().Text.Should().Be("efg");
    }

    [Theory]
    [InlineData("\ud83d\ude00\ud83d\ude00", 3, 2)]
    [InlineData("e\u0301e\u0301", 1, 2)]
    [InlineData("\u754c\u754c", 3, 2)]
    [InlineData("\ud83d\udc69\u200d\ud83d\udcbbX", 2, 2)]
    public void Wrapping_PreservesTextElements(string text, int width, int expectedRows)
    {
        var line = Line(text);
        var index = line.GetIndex(width);
        index.Count.Should().Be(expectedRows);
        string reconstructed = string.Concat(Enumerable.Range(0, index.Count).Select(row =>
        {
            var range = index.GetRow(row);
            return line.Text[range.Start..range.End];
        }));
        reconstructed.Should().Be(text);
    }

    [Fact]
    public void DisplayControlSequences_AreInertText()
    {
        var line = Line("before\u001b[?1049l\u0007after");
        line.Text.Should().NotContain("\u001b").And.NotContain("\u0007");
        line.Text.Should().Contain("\\u001b");
    }

    [Fact]
    public void Clear_DoesNotReuseEntryIdentities()
    {
        var buffer = new CompactLogBuffer();
        buffer.Add(Line("first"));
        long id = buffer.Capture().Entries.Single().Id;
        buffer.Clear();
        buffer.Add(Line("second"));
        buffer.Capture().Entries.Single().Id.Should().BeGreaterThan(id);
    }

    [Fact]
    public void SustainedInput_PlateausAtTheTextBudget_AndOnlyMaterializesTheViewport()
    {
        var buffer = new CompactLogBuffer();
        string payload = new('x', 1024);
        for (int index = 0; index < 20_000; index++)
        {
            buffer.Add(Line(payload));
        }

        var retained = buffer.Capture();
        retained.Entries.Should().HaveCount(8192);
        retained.TextBytes.Should().Be(CompactLogBuffer.DefaultTextBudget);
        retained.Evicted.Should().Be(20_000 - 8192);

        var viewport = new LogViewport();
        viewport.Refresh(retained, 80, 20, new LogFilter());
        viewport.RenderRows(new Azure.Functions.Cli.Console.Theme.DefaultTheme().Selection).Should().HaveCount(20);
        viewport.Scroll(int.MaxValue);
        viewport.TopRow.Should().Be(0);
        viewport.TotalRows.Should().BeGreaterThan(100_000);
    }

    [Theory]
    [InlineData("\uffff")]
    [InlineData("\ud83f\udffe")]
    [InlineData("\ud800")]
    public void Formatter_NormalizesUnrenderableUnicodeBeforeSpectre(string invalid)
    {
        var formatter = new CompactLogLineFormatter(new Azure.Functions.Cli.Console.Theme.DefaultTheme(),
            new Azure.Functions.Cli.Hosting.Dashboard.FunctionPalette());
        var entry = new Azure.Functions.Cli.Hosting.Events.HostLogEntry(DateTimeOffset.UnixEpoch, "Host.Test",
            LogLevel.Information, default, "before " + invalid + " after", null,
            Azure.Functions.Cli.Hosting.Events.HostLogEntry.EmptyAttributes);

        var line = formatter.Format(entry, [], null);

        line.Should().NotBeNull();
        line!.Text.Should().Contain("before").And.Contain("after").And.Contain("\ufffd");
    }

    private static CompactLogLine Line(string text, string? function = null)
        => new(new Text(text), function, false, LogLevel.Information);
}
