// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Substitutable view of the host operating system. Keeps the discovery code
/// off <see cref="OperatingSystem"/> static helpers so tests can exercise both
/// Windows and Unix branches on any platform.
/// </summary>
internal interface IPlatform
{
    public bool IsWindows { get; }
}
