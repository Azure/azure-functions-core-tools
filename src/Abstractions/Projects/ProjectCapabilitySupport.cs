// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes whether a language stack reports support for one project capability, not a template's aggregate compatibility.
/// </summary>
public enum ProjectCapabilitySupport
{
    /// <summary>
    /// Support for the capability is not established for the captured project.
    /// </summary>
    Unsupported,

    /// <summary>
    /// The language stack established from captured project inputs that the project supports the capability.
    /// </summary>
    Supported,
}
