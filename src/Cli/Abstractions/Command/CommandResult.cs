﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/CommandResult.cs
using System.Diagnostics;

namespace Azure.Functions.Cli.Abstractions
{
    public readonly struct CommandResult(ProcessStartInfo startInfo, int exitCode, string? stdOut, string? stdErr)
    {
        public static readonly CommandResult Empty = default(CommandResult);

        public ProcessStartInfo StartInfo { get; } = startInfo;

        public int ExitCode { get; } = exitCode;

        public string? StdOut { get; } = stdOut;

        public string? StdErr { get; } = stdErr;
    }
}
