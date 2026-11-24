// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Init;

/// <summary>
/// The initialization state of a <c>func init</c> target directory.
/// </summary>
internal enum InitializationState
{
    // No Functions project markers present; safe to scaffold from scratch.
    Empty,

    // host.json present but the v5 config (.func/config.json) is missing.
    // Treat as a pre-v5 project we can adopt by writing the config.
    AdoptableExistingProject,

    // .func/config.json exists with a runtime but no language on a
    // multi-language stack. Run the adopt-mode language detector and
    // merge the result back into the existing config without touching
    // other keys. Single-language stacks never enter this state because
    // language is intentionally omitted for them.
    PartiallyInitialized,

    // .func/config.json is present and complete. Refuse without --force.
    FullyInitialized,
}
