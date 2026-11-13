// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Paints a bounded cell grid without relative cursor history or a bottom-row newline.
/// </summary>
internal sealed class DashboardFrame(int width, int height, IRenderable header, int headerRows,
    IRenderable logHeader, IReadOnlyList<IRenderable> logs, IRenderable footer) : Renderable
{
    private readonly int _width = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    private readonly int _height = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));
    private readonly IRenderable _header = header ?? throw new ArgumentNullException(nameof(header));
    private readonly IRenderable _logHeader = logHeader ?? throw new ArgumentNullException(nameof(logHeader));
    private readonly IReadOnlyList<IRenderable> _logs = logs ?? throw new ArgumentNullException(nameof(logs));
    private readonly IRenderable _footer = footer ?? throw new ArgumentNullException(nameof(footer));

    public int LogTop => Math.Min(Math.Max(0, headerRows), Math.Max(0, _height - 2)) + 1;
    public int LogHeight => Math.Max(0, _height - LogTop - 1);

    protected override Measurement Measure(RenderOptions options, int maxWidth) => new(_width, _width);

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        int width = Math.Min(_width, maxWidth);
        int headerHeight = LogTop - 1;
        List<SegmentLine> heading = Segment.SplitLines(_header.Render(options with { Height = null }, width));
        List<SegmentLine> footer = Segment.SplitLines(_footer.Render(options with { Height = null }, int.MaxValue));
        for (int row = 0; row < _height; row++)
        {
            if (options.Ansi)
            {
                yield return Segment.Control(string.Create(CultureInfo.InvariantCulture, $"\u001b[{row + 1};1H"));
            }
            else if (row > 0)
            {
                yield return Segment.LineBreak;
            }

            IEnumerable<Segment> content = row == _height - 1 ? footer.FirstOrDefault() ?? []
                : row < headerHeight ? row < heading.Count ? heading[row] : []
                : row == headerHeight ? _logHeader.Render(options, width)
                : row - LogTop < _logs.Count ? _logs[row - LogTop].Render(options, width)
                : [];
            int column = 0;
            foreach (Segment segment in content)
            {
                if (segment.IsControlCode)
                {
                    continue;
                }

                string text = LogText.Sanitize(segment.Text);
                for (int offset = 0; offset < text.Length && text[offset] != '\n';)
                {
                    int length = LogText.ElementLength(text, offset);
                    int cells = LogText.CellWidth(text, offset, length, column);
                    if (column + cells > width)
                    {
                        break;
                    }

                    yield return text[offset] == '\t'
                        ? new Segment(new string(' ', cells), segment.Style)
                        : new Segment(text.Substring(offset, length), segment.Style);
                    column += cells;
                    offset += length;
                }
            }

            if (column < width)
            {
                yield return Segment.Padding(width - column);
            }
        }
    }
}
