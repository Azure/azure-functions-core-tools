// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Tracks compact dashboard input state shared by rendering and key handling.
/// </summary>
internal sealed class CompactInputState
{
    public bool HelpOpen { get; set; }

    public bool FunctionBrowserOpen { get; set; }

    public bool FunctionSearchOpen { get; set; }

    public int FunctionBrowserSelectedIndex { get; set; }

    public int FunctionBrowserRowOffset { get; set; }

    public int FunctionSearchSelectedIndex { get; set; }

    public int FunctionSearchRowOffset { get; set; }

    public string FunctionSearchQuery { get; set; } = string.Empty;

    public string? ActiveFunctionFilter { get; set; }

    public int LogScrollOffset { get; set; }

    public bool ErrorsOnly { get; set; }

    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;
}
