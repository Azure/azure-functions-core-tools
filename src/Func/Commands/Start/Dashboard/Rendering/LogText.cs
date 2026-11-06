// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

internal static class LogText
{
    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        const int limit = CompactLogBuffer.DefaultTextBudget / sizeof(char);
        bool needsConversion = value.Length > limit;
        for (int index = 0; index < value.Length && !needsConversion; index++)
        {
            char character = value[index];
            needsConversion = character is '\r' or '\ufffe' or '\uffff'
                || (char.IsControl(character) && character is not ('\n' or '\t'));
            if (char.IsSurrogate(character))
            {
                if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    Rune.TryCreate(character, value[index + 1], out Rune rune);
                    needsConversion = (rune.Value & 0xffff) is 0xfffe or 0xffff;
                    index++;
                }
                else
                {
                    needsConversion = true;
                }
            }
        }

        if (!needsConversion)
        {
            return value;
        }

        StringBuilder text = new(Math.Min(value.Length, limit));
        int offset = 0;
        while (offset < value.Length && text.Length <= limit)
        {
            Rune.DecodeFromUtf16(value.AsSpan(offset), out Rune rune, out int consumed);
            offset += Math.Max(1, consumed);
            if (rune.Value == '\r')
            {
                if (offset < value.Length && value[offset] == '\n')
                {
                    offset++;
                }

                text.Append('\n');
            }
            else if ((rune.Value & 0xffff) is 0xfffe or 0xffff)
            {
                text.Append('\ufffd');
            }
            else if (Rune.IsControl(rune) && rune.Value is not ('\n' or '\t'))
            {
                text.Append("\\u").Append(rune.Value.ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(rune.ToString());
            }
        }

        string result = text.ToString();
        if (result.Length > limit || offset < value.Length)
        {
            const string marker = " [display truncated]";
            result = result[..BoundaryBefore(result, limit - marker.Length)] + marker;
        }

        return result;
    }

    public static string EscapeMarkup(string value) => Markup.Escape(Sanitize(value));

    public static int Measure(string value)
    {
        string text = Sanitize(value).Replace('\n', ' ');
        int column = 0;
        for (int offset = 0; offset < text.Length;)
        {
            int length = ElementLength(text, offset);
            column += CellWidth(text, offset, length, column);
            offset += length;
        }

        return column;
    }

    public static string Clip(string value, int width)
    {
        string text = Sanitize(value).Replace('\n', ' ');
        if (width <= 0)
        {
            return string.Empty;
        }

        int column = 0;
        int cut = 0;
        for (int offset = 0; offset < text.Length;)
        {
            int length = ElementLength(text, offset);
            int cells = CellWidth(text, offset, length, column);
            if (column + cells > width)
            {
                return text[..cut] + "\u2026";
            }

            offset += length;
            column += cells;
            if (column < width)
            {
                cut = offset;
            }
        }

        return text;
    }

    public static RenderOptions CreateRenderOptions(int width, int height)
    {
        Profile profile = new(new AnsiConsoleOutput(TextWriter.Null), Encoding.UTF8);
        profile.Capabilities.Unicode = true;
        profile.Capabilities.Ansi = true;
        return new RenderOptions(profile.Capabilities, new Size(width, height));
    }

    public static int ElementLength(string text, int offset)
        => text[offset] < 128 && (offset + 1 == text.Length || text[offset + 1] < 128)
            ? 1
            : StringInfo.GetNextTextElementLength(text, offset);

    public static int CellWidth(string text, int offset, int length, int column)
    {
        char first = text[offset];
        if (first == '\t')
        {
            return 8 - (column % 8);
        }

        if (length == 1 && first < 128)
        {
            return 1;
        }

        ReadOnlySpan<char> element = text.AsSpan(offset, length);
        Rune.DecodeFromUtf16(element, out Rune rune, out _);
        if (rune.Value is >= 0x1f000 and <= 0x1faff || element.Contains('\ufe0f') || element.Contains('\u200d'))
        {
            return 2;
        }

        return Math.Max(0, new Segment(element.ToString()).CellCount());
    }

    public static int BoundaryBefore(string text, int length)
    {
        int offset = 0;
        while (offset < text.Length)
        {
            int next = offset + ElementLength(text, offset);
            if (next > length)
            {
                break;
            }

            offset = next;
        }

        return offset;
    }
}
