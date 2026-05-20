// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Context passed to project cleanup after a host run completes.
/// </summary>
public sealed record FunctionsProjectHostRunCompletionContext(
    FunctionsProjectHostRunContext RunContext,
    FunctionsProjectHostRunOutcome Outcome);
