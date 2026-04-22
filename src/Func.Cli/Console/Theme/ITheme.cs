// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Spectre.Console;

namespace Azure.Functions.Cli.Console.Theme;

/// <summary>
/// Visual theme for CLI output. Provides semantic <see cref="Style"/> values so
/// that callers reference roles (command, path, muted…) instead of colors.
/// Swapping implementations (e.g. a no-color theme) requires no changes in
/// command or help code.
/// </summary>
public interface ITheme
{
    /// <summary>Product and section titles (e.g. "Azure Functions CLI").</summary>
    public Style Title { get; }

    /// <summary>Section headers rendered as rules (e.g. "Usage", "Commands").</summary>
    public Style Heading { get; }

    /// <summary>Command, subcommand, and option names (e.g. <c>func new</c>).</summary>
    public Style Command { get; }

    /// <summary>Argument placeholders like <c>&lt;command&gt;</c>.</summary>
    public Style Placeholder { get; }

    /// <summary>Optional argument placeholders like <c>[path]</c>.</summary>
    public Style OptionalArg { get; }

    /// <summary>File system paths.</summary>
    public Style Path { get; }

    /// <summary>Descriptions, hints, and secondary text.</summary>
    public Style Muted { get; }

    /// <summary>Inline code, literal values, and URLs in prose.</summary>
    public Style Code { get; }

    /// <summary>Emphasized text (version numbers, highlighted tokens).</summary>
    public Style Emphasis { get; }

    /// <summary>Successful operation indicators.</summary>
    public Style Success { get; }

    /// <summary>Error messages.</summary>
    public Style Error { get; }

    /// <summary>Warning messages.</summary>
    public Style Warning { get; }
}
