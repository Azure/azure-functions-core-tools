// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Render-ready compact dashboard log row plus the metadata needed for filtering.
/// </summary>
internal sealed record CompactLogLine(IRenderable Renderable, string? FunctionName, bool IsError, LogLevel Level);
