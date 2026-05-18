// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Describes the renderer side effects requested by compact input handling.
/// </summary>
internal readonly record struct CompactInputResult(bool Handled, bool ClearLogsRequested, bool ShutdownRequested);
