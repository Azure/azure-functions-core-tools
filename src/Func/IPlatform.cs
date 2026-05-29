// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli;

/// <summary>
/// Provides a substitutable view of the current operating system.
/// </summary>
internal interface IPlatform
{
    public bool IsWindows { get; }

    public bool IsMacOS { get; }
}
