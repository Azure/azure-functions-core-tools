// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Spectre.Console;

namespace Azure.Functions.Cli.Console.Theme;

/// <summary>
/// Default color theme. Preserves the palette used prior to the theming refactor.
/// </summary>
internal sealed class DefaultTheme : ITheme
{
    public Style Title { get; } = new(Color.DarkGoldenrod, decoration: Decoration.Bold);

    public Style Heading { get; } = new(Color.Blue);

    public Style Command { get; } = new(Color.White);

    public Style Placeholder { get; } = new(Color.Cyan1);

    public Style OptionalArg { get; } = new(Color.Cyan1);

    public Style Path { get; } = new(Color.Cyan1);

    public Style Muted { get; } = new(Color.Grey58);

    public Style OptionsPlaceholder { get; } = new(Color.Grey66);

    public Style Code { get; } = new(Color.White);

    public Style Emphasis { get; } = new(decoration: Decoration.Bold);

    public Style Success { get; } = new(Color.Green, decoration: Decoration.Bold);

    public Style Error { get; } = new(Color.Red, decoration: Decoration.Bold);

    public Style Warning { get; } = new(Color.Yellow, decoration: Decoration.Bold);

    public Style Active { get; } = new(Color.Cyan1);

    public Style Hyperlink { get; } = new(Color.Blue, decoration: Decoration.Underline);
}
