// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Render-ready compact dashboard log row plus the metadata needed for filtering.
/// </summary>
internal sealed class CompactLogLine
{
    private readonly Func<int, IReadOnlyList<IRenderable>> _renderRows;

    public CompactLogLine(IRenderable renderable, string? functionName, bool isError, LogLevel level)
        : this(renderable, _ => [renderable], functionName, isError, level)
    {
    }

    public CompactLogLine(
        IRenderable renderable,
        Func<int, IReadOnlyList<IRenderable>> renderRows,
        string? functionName,
        bool isError,
        LogLevel level)
    {
        Renderable = renderable ?? throw new ArgumentNullException(nameof(renderable));
        _renderRows = renderRows ?? throw new ArgumentNullException(nameof(renderRows));
        FunctionName = functionName;
        IsError = isError;
        Level = level;
    }

    public IRenderable Renderable { get; }

    public string? FunctionName { get; }

    public bool IsError { get; }

    public LogLevel Level { get; }

    public IReadOnlyList<IRenderable> RenderRows(int width)
    {
        IReadOnlyList<IRenderable> rows = _renderRows(Math.Max(1, width));
        return rows.Count == 0 ? [Renderable] : rows;
    }
}
