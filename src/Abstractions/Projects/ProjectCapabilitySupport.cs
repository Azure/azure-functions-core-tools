// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes a stack implementation's knowledge of one project capability, not a template's aggregate compatibility.
/// </summary>
public enum ProjectCapabilitySupport
{
    /// <summary>
    /// The stack implementation has not established whether the project supports the capability.
    /// </summary>
    Unknown,

    /// <summary>
    /// The stack implementation reports that the project supports the capability.
    /// </summary>
    Supported,

    /// <summary>
    /// The stack implementation reports that the project does not support the capability.
    /// </summary>
    Unsupported,
}
