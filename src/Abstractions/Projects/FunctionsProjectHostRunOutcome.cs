// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes how a host run ended.
/// </summary>
public abstract record FunctionsProjectHostRunOutcome
{
    private FunctionsProjectHostRunOutcome()
    {
    }

    public sealed record Completed(int ExitCode) : FunctionsProjectHostRunOutcome;

    public sealed record Canceled : FunctionsProjectHostRunOutcome;

    public sealed record Failed(Exception Exception) : FunctionsProjectHostRunOutcome;
}
