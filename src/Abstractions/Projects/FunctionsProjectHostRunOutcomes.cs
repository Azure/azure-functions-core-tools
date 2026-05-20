// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Creates <see cref="FunctionsProjectHostRunOutcome"/> instances.
/// </summary>
public static class FunctionsProjectHostRunOutcomes
{
    public static FunctionsProjectHostRunOutcome Completed(int exitCode)
        => new FunctionsProjectHostRunOutcome.Completed(exitCode);

    public static FunctionsProjectHostRunOutcome Canceled()
        => new FunctionsProjectHostRunOutcome.Canceled();

    public static FunctionsProjectHostRunOutcome Failed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new FunctionsProjectHostRunOutcome.Failed(exception);
    }
}
