// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Console;

/// <summary>
/// A fluent, theme-aware builder for a single line of styled text.
/// Each role-named method (e.g. <see cref="Command"/>, <see cref="Muted"/>)
/// appends a text segment styled with the theme's value for that role.
/// Literal text is escaped automatically — callers never write Spectre markup.
/// </summary>
/// <remarks>
/// Typical use via <c>IInteractionService.WriteLine(l =&gt; l.Muted("Run ").Command("func new"))</c>.
/// The builder itself is just a segment accumulator; rendering is performed by the interaction service.
/// </remarks>
internal sealed class InlineLine
{
    private readonly ITheme _theme;
    private readonly List<Segment> _segments = new();

    internal InlineLine(ITheme theme)
    {
        _theme = theme;
    }

    /// <summary>Gets the theme bound to this builder (for ad-hoc styled segments).</summary>
    public ITheme Theme => _theme;

    /// <summary>Appends unstyled text.</summary>
    public InlineLine Plain(string text) => Add(text, null);

    /// <summary>Appends text with an ad-hoc style (escape hatch — prefer role-named methods).</summary>
    public InlineLine Styled(string text, Style style) => Add(text, style);

    public InlineLine Title(string text) => Add(text, _theme.Title);

    public InlineLine Heading(string text) => Add(text, _theme.Heading);

    public InlineLine Command(string text) => Add(text, _theme.Command);

    public InlineLine Placeholder(string text) => Add(text, _theme.Placeholder);

    public InlineLine OptionalArg(string text) => Add(text, _theme.OptionalArg);
    public InlineLine Path(string text) => Add(text, _theme.Path);

    public InlineLine Muted(string text) => Add(text, _theme.Muted);

    public InlineLine Code(string text) => Add(text, _theme.Code);

    public InlineLine Emphasis(string text) => Add(text, _theme.Emphasis);

    public InlineLine Success(string text) => Add(text, _theme.Success);

    public InlineLine Error(string text) => Add(text, _theme.Error);

    public InlineLine Warning(string text) => Add(text, _theme.Warning);

    /// <summary>Appends a single space (shorthand for <c>Plain(" ")</c>).</summary>
    public InlineLine Space() => Add(" ", null);

    /// <summary>
    /// Materializes the accumulated segments into a renderable paragraph.
    /// Text is escaped at segment boundaries so raw brackets in content are
    /// preserved verbatim.
    /// </summary>
    public IRenderable ToRenderable()
    {
        var paragraph = new Paragraph();
        foreach (var segment in _segments)
        {
            paragraph.Append(segment.Text, segment.Style ?? Style.Plain);
        }
        return paragraph;
    }

    /// <summary>Returns the concatenated unstyled text (for testing and logging).</summary>
    public string ToPlainString()
        => string.Concat(_segments.Select(s => s.Text));

    private InlineLine Add(string text, Style? style)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _segments.Add(new Segment(text, style));
        }
        return this;
    }

    private readonly record struct Segment(string Text, Style? Style);
}
