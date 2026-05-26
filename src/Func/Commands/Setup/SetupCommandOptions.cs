// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Setup;

internal sealed record SetupCommandOptions(
    DirectoryInfo WorkingDirectory,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> ProfileNames,
    string? Source,
    SetupInstallPolicy InstallPolicy,
    bool IncludePrerelease,
    bool NonInteractive,
    bool AssumeYes,
    bool Check,
    SetupOutputMode OutputMode);

internal enum SetupInstallPolicy
{
    LatestCompatible,
    IfNeeded,
}

internal enum SetupOutputMode
{
    Plain,
    Json,
}

internal sealed record SetupRunResult(int ExitCode);

internal interface ISetupRunner
{
    public Task<SetupRunResult> RunAsync(SetupCommandOptions options, CancellationToken cancellationToken);
}
