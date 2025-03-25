// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Note that this file is copied from: https://github.com/dotnet/sdk
// Once the dotnet cli utils package is in a published consumable state, we will migrate over to use that

using System.Diagnostics;

public readonly struct CommandResult(ProcessStartInfo startInfo, int exitCode, string? stdOut, string? stdErr)
{
    public static readonly CommandResult Empty = new();

    public ProcessStartInfo StartInfo { get; } = startInfo;
    public int ExitCode { get; } = exitCode;
    public string? StdOut { get; } = stdOut;
    public string? StdErr { get; } = stdErr;
}
